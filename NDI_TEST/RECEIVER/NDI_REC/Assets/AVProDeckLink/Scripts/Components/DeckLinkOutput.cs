using UnityEngine;
using System.Collections;
using System;
using System.Runtime.InteropServices;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink
{
	[StructLayout(LayoutKind.Sequential, Size = 24)]
	public struct ComputeBufferParams
	{
		public uint width;
		public uint height;
		public uint bufferWidth;
		public uint bigEndian;
		public uint leading;
		public uint isLinear;

		public static int Size()
		{
			return 24;
		}
	}

    [AddComponentMenu("AVPro DeckLink/DeckLinkOutput")]
    public class DeckLinkOutput : DeckLink
    {
		//Anti-Aliasing
        public enum AALevel
        {
            None = 1,
            Two = 2,
            Four = 4,
            Eight = 8
        }

		public DeckLinkInput _syncedToInput;

		public AALevel _antiAliasingLevel = AALevel.Two;

		//buffering & timing
		[Range(2, 9)]
		public int _bufferBalance = 2;

		public enum NoCameraMode
		{
			None,
			Colour,
			DefaultTexture
		}

		public NoCameraMode _noCameraMode;
		public Texture _defaultTexture;
		public Color _defaultColour;

		private int _outputFrameRate = -1;
		private int _currFrame = 0;
		private bool _canOutputFrame = false;
		private int _targetFrameRate = -1;
		private float prevFrameTime = 0.0f;
		private float currFrameTime = 0.0f;
		private float _timeSinceLastFrame = 0f;
		private bool _current3DEnabled;

		//pipeline textures/
		public Camera _camera;
		public Camera _rightEyeCamera;
		public bool _lowLatencyMode = false;

		private Shader _rgbaToYuv422Shader;
		private Shader _rgbaToYuv422Shader2;
		private Shader _rgbaToBgraShader;
		private Shader _rgbaToArgbShader;
		private Shader _interlaceShader;
		private Shader _blendShader;
		private ComputeShader _abgrTo10bitARGB;

		//left
		private RenderTexture _inputTexture;
		private RenderTexture[] _capturedFrames = null;
		private RenderTexture _convertedTexture;
		private byte[] _outputBuffer = null;
		private RenderTexture _blended;

		//right
		private RenderTexture _rightInputTexture;
		private RenderTexture[] _rightCapturedFrames = null;
		private RenderTexture _rightConvertedTexture;
		private byte[] _rightOutputBuffer = null;
		private RenderTexture _rightBlended;
		
		private RenderTexture _interlacedTexture;

		private ComputeBuffer _convertedCompBuffer = null;
		private ComputeBuffer _parameters = null;
		
		private Material _interlaceMaterial;
		private Material _conversionMaterial;
		private Material _blendMat;

		private DeckLinkPlugin.PixelFormat _format = DeckLinkPlugin.PixelFormat.Unknown;
		private bool _interlaced;
		private int _interlacePass = 0;
		private IntPtr _convertedPointer = IntPtr.Zero;
		private IntPtr _rightConvertedPointer = IntPtr.Zero;

		private byte _currCapturedFrame = 0;

		//Audio
		public AudioSource _outputAudioSource;
		public bool _muteOutputAudio = false;

		private DeckLinkAudioOutput _audioOutputManager = null;

		public bool _bypassGamma = false;

		//misc
		public int _genlockPixelOffset = 0;

		private static int refCount = 0;
        private static int prevRefCount;

		private const string ShaderKeyRec709 = "USE_REC709";
		private const string ShaderKeyRec2100 = "USE_REC2100";
		private const string ShaderKeyRec2020 = "USE_REC2020";
		private const string ShaderKeyIgnoreAlpha = "IGNORE_ALPHA";

		public RenderTexture InputTexture
        {
            get { return _inputTexture; }
        }

		public RenderTexture RightInputTexture
		{
			get { return _rightInputTexture; }
		}

        public enum KeyerMode
        {
            None = 0,
            Internal,
            External,
        }

		public KeyerMode _keyerMode = KeyerMode.None;

		protected override void Init()
		{
			base.Init();

			_current3DEnabled = _enable3D;
		}

		private void FindShaders()
		{
			_rgbaToYuv422Shader = Shader.Find("AVProDeckLink/RGBA 4:4:4 to UYVY 4:2:2");
			_rgbaToYuv422Shader2 = Shader.Find("AVProDeckLink/RGBA 4:4:4 to UYVY 4:2:2 10-bit");
			_rgbaToBgraShader = Shader.Find("AVProDeckLink/RGBA 4:4:4 to BGRBA 4:4:4");
			_rgbaToArgbShader = Shader.Find("AVProDeckLink/RGBA 4:4:4 to ARGB 4:4:4");
			_interlaceShader = Shader.Find("AVProDeckLink/Interlacer");
			_blendShader = Shader.Find("AVProDeckLink/BlendFrames");
			_abgrTo10bitARGB = (ComputeShader)Resources.Load("Shaders/AVProDeckLink_RGBA_to_10RGBX");
		}

		private void InitializeAudioOutput()
		{
			DeckLinkAudioOutput[] audioOutputs = FindObjectsOfType<DeckLinkAudioOutput>();
			if (audioOutputs.Length > 1)
			{
				Debug.LogError("[AVProDeckLink] There should never be more than one DeckLinkAudioOutput object per scene");
			}
			else if (audioOutputs.Length == 1)
			{
				_audioOutputManager = audioOutputs[0];
			}
			else
			{
				if (_outputAudioSource == null)
				{
					AudioListener[] listeners = FindObjectsOfType<AudioListener>();

					GameObject listenerObject;

					if (listeners.Length == 0)
					{
						listenerObject = new GameObject("[AVProDeckLink]Listener");
						listenerObject.AddComponent<AudioListener>();
					}
					else
					{
						listenerObject = listeners[0].gameObject;
					}

					_audioOutputManager = listenerObject.AddComponent<DeckLinkAudioOutput>();

#if UNITY_5 && (UNITY_5_1 || UNITY_5_2)
					// TODO: comment why this is here?
                    DeckLinkAudioOutput temp = listenerObject.AddComponent<DeckLinkAudioOutput>();
                    Destroy(temp);
#endif
				}
				else
				{
					_audioOutputManager = _outputAudioSource.gameObject.AddComponent<DeckLinkAudioOutput>();
				}
			}
		}

		private void UpdateReferenceCounter()
		{
			if (refCount == 0)
			{
				prevRefCount = QualitySettings.vSyncCount;
				QualitySettings.vSyncCount = 0;
			}

			refCount++;
		}

		public override void Awake()
		{
			base.Awake();

			UpdateReferenceCounter();
			FindShaders();
			InitializeAudioOutput();
		}

		private void InitCaptureBlendResources(int width, int height)
		{
			if(_capturedFrames != null)
			{
				foreach(var frame in _capturedFrames)
				{
					RenderTexture.ReleaseTemporary(frame);
				}
				_capturedFrames = null;
			}

			if(_rightCapturedFrames != null)
			{
				foreach(var frame in _rightCapturedFrames)
				{
					RenderTexture.ReleaseTemporary(frame);
				}
				_rightCapturedFrames = null;
			}

			if(_blended != null && (_blended.width != width || _blended.height != height || _blended.antiAliasing != (int)_antiAliasingLevel))
			{
				RenderTexture.ReleaseTemporary(_blended);
				_blended = null;
			}

			if(_rightBlended != null && (!_current3DEnabled || _rightBlended.width != width || _rightBlended.height != height || _rightBlended.antiAliasing != (int)_antiAliasingLevel))
			{
				RenderTexture.ReleaseTemporary(_rightBlended);
				_rightBlended = null;
			}

			RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGB32;
			if (_useHdr)
			{
				renderTextureFormat = RenderTextureFormat.ARGBFloat;
			}

			if (DeckLinkSettings.Instance._multiOutput)
			{
				_capturedFrames = new RenderTexture[2];
				for(int i = 0; i < 2; ++i)
				{
					_capturedFrames[i] = RenderTexture.GetTemporary(width, height, 0, renderTextureFormat,
						/*QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : */RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
				}

				if(_current3DEnabled)
				{
					_rightCapturedFrames = new RenderTexture[2];
					for (int i = 0; i < 2; ++i)
					{
						_rightCapturedFrames[i] = RenderTexture.GetTemporary(width, height, 0, renderTextureFormat, RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
					}
				}
			}

			if(_blended == null)
			{
				_blended = RenderTexture.GetTemporary(width, height, 0, renderTextureFormat,
					/*QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : */RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
			}

			if(_rightBlended == null && _current3DEnabled)
			{
				_rightBlended = RenderTexture.GetTemporary(width, height, 0, renderTextureFormat, RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
			}

			if (_blendMat == null)
			{
				_blendMat = new Material(_blendShader);
			}

			if (_inputTexture != null)
			{
				if (_inputTexture.width != width || _inputTexture.height != height || _inputTexture.antiAliasing != (int)_antiAliasingLevel)
				{
					RenderTexture.ReleaseTemporary(_inputTexture);
					_inputTexture = null;
				}
			}

			if(_rightInputTexture != null)
			{
				if(!_current3DEnabled || _rightInputTexture.width != width || _rightInputTexture.height != height || _rightInputTexture.antiAliasing != (int)_antiAliasingLevel)
				{
					RenderTexture.ReleaseTemporary(_rightInputTexture);
					_rightInputTexture = null;
				}
			}

			if (_inputTexture == null)
			{
				_inputTexture = RenderTexture.GetTemporary(width, height, 24, renderTextureFormat,
					/*QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : */RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
			}

			if(_current3DEnabled && _rightInputTexture == null)
			{
				_rightInputTexture = RenderTexture.GetTemporary(width, height, 24, renderTextureFormat, RenderTextureReadWrite.Default, (int)_antiAliasingLevel);
			}

			if (!_inputTexture.IsCreated())
			{
				_inputTexture.Create();
			}

			if(_rightInputTexture != null && !_rightInputTexture.IsCreated())
			{
				_rightInputTexture.Create();
			}
		}

        private void InitConversionResources(DeckLinkPlugin.PixelFormat format, int width, int height)
        {
            if (_conversionMaterial != null || _format != format)
            {
                Material.Destroy(_conversionMaterial);
                _conversionMaterial = null;
                _format = format;
            }

			int texWidth = -1;
			// If we are doing keying and a non-RGBA mode is used for output, then use an RGBA texture, 
			// as this conversion will be handled by the DeckLink hardware.
			if (_keyerMode != KeyerMode.None && !HasAlphaChannel(format))
			{
				_conversionMaterial = new Material(_rgbaToArgbShader);
				texWidth = width;
			}
			else
			{
				// Otherwise convert to the output format
				switch (format)
				{
					case DeckLinkPlugin.PixelFormat.YCbCr_8bpp_422:
						_conversionMaterial = new Material(_rgbaToYuv422Shader);
						texWidth = width / 2;
						break;
					case DeckLinkPlugin.PixelFormat.YCbCr_10bpp_422:
						_conversionMaterial = new Material(_rgbaToYuv422Shader2);
						texWidth = (width / 6) * 4;
						_conversionMaterial.SetFloat("_TextureWidth", texWidth);
						break;
					case DeckLinkPlugin.PixelFormat.BGRA_8bpp_444:
						_conversionMaterial = new Material(_rgbaToBgraShader);
						texWidth = width;
						break;
					case DeckLinkPlugin.PixelFormat.ARGB_8bpp_444:
						_conversionMaterial = new Material(_rgbaToArgbShader);
						texWidth = width;
						break;
					default:
						break;
				}
			}

			if (_parameters != null)
			{
				_parameters.Release();
				_parameters = null;
			}

			DeckLinkPlugin.SetOutputBufferPointer(_deviceIndex, null);
			DeckLinkPlugin.SetOutputTexturePointer(_deviceIndex, IntPtr.Zero);
			DeckLinkPlugin.SetRightOutputBufferPointer(_deviceIndex, null);
			DeckLinkPlugin.SetRightOutputTexturePointer(_deviceIndex, IntPtr.Zero);

			if (_convertedTexture != null)
			{
				RenderTexture.ReleaseTemporary(_convertedTexture);
				_convertedTexture = null;
				_convertedPointer = IntPtr.Zero;
			}

			if(_rightConvertedTexture != null)
			{
				RenderTexture.ReleaseTemporary(_rightConvertedTexture);
				_rightConvertedTexture = null;
				_rightConvertedPointer = IntPtr.Zero;
			}

			if(_outputBuffer != null)
			{
				_outputBuffer = null;
			}

			if(_rightOutputBuffer != null)
			{
				_rightOutputBuffer = null;
			}

			if(_convertedCompBuffer != null)
			{
				_convertedCompBuffer.Release();
				_convertedCompBuffer = null;
			}

			if (texWidth < 0)
			{
				//sets up compute buffers 
				if (_format == DeckLinkPlugin.PixelFormat.RGBX_10bpp_444 || 
					_format == DeckLinkPlugin.PixelFormat.RGBX_10bpp_444_LE ||
					_format == DeckLinkPlugin.PixelFormat.RGB_10bpp_444)
				{
					_parameters = new ComputeBuffer(1, ComputeBufferParams.Size());

					ComputeBufferParams[] parms = new ComputeBufferParams[1];
					parms[0].height = (uint)height;
					parms[0].width = (uint)width;
					parms[0].bufferWidth = (uint)(width + 63) / 64 * 64;
					parms[0].leading = _format == DeckLinkPlugin.PixelFormat.RGB_10bpp_444 ? 1U : 0U;
					bool formatBigEndian = _format != DeckLinkPlugin.PixelFormat.RGBX_10bpp_444_LE ? true : false;
					if(BitConverter.IsLittleEndian)
					{
						formatBigEndian = !formatBigEndian;
					}
					parms[0].bigEndian = formatBigEndian ? 1U : 0U;
					parms[0].isLinear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1U : 0U;

					_outputBuffer = new byte[parms[0].bufferWidth * parms[0].height * 4];
					if (_current3DEnabled)
					{
						_rightOutputBuffer = new byte[parms[0].bufferWidth * parms[0].height * 4];
					}

					_convertedCompBuffer = new ComputeBuffer((int)(parms[0].bufferWidth * parms[0].height), 4, ComputeBufferType.Raw);

					_parameters.SetData(parms);

					DeckLinkPlugin.SetOutputBufferPointer(_deviceIndex, _outputBuffer);
					DeckLinkPlugin.SetRightOutputBufferPointer(_deviceIndex, _rightOutputBuffer);
				}
				else
				{
					RenderTextureReadWrite readWriteMode = (QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);

					_convertedTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWriteMode, 1);
					_convertedPointer = _convertedTexture.GetNativeTexturePtr();
					DeckLinkPlugin.SetOutputTexturePointer(_deviceIndex, _convertedPointer);

					if(_current3DEnabled)
					{
						_rightConvertedTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWriteMode, 1);
						_rightConvertedPointer = _rightConvertedTexture.GetNativeTexturePtr();
						DeckLinkPlugin.SetRightOutputTexturePointer(_deviceIndex, _rightConvertedPointer);
					}
				}
			}
			else
			{
				RenderTextureReadWrite readWriteMode = (QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);

				RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
				if (format == DeckLinkPlugin.PixelFormat.YCbCr_10bpp_422)
				{
					textureFormat = RenderTextureFormat.ARGB2101010;
				}

				_convertedTexture = RenderTexture.GetTemporary(texWidth, height, 0, textureFormat, readWriteMode, 1);
				_convertedPointer = _convertedTexture.GetNativeTexturePtr();
				DeckLinkPlugin.SetOutputTexturePointer(_deviceIndex, _convertedPointer);

				if(_current3DEnabled)
				{
					_rightConvertedTexture = RenderTexture.GetTemporary(texWidth, height, 0, textureFormat, readWriteMode, 1);
					_rightConvertedPointer = _rightConvertedTexture.GetNativeTexturePtr();
					DeckLinkPlugin.SetRightOutputTexturePointer(_deviceIndex, _rightConvertedPointer);
				}
			}
        }

        private void InitInterlaceResources(int width, int height)
        {
            if (_interlaceMaterial == null)
            {
                _interlaceMaterial = new Material(_interlaceShader);
            }

            _interlaceMaterial.SetFloat("_TextureHeight", height);

            if (_interlacedTexture != null)
            {
                if (_interlacedTexture.width != width || _interlacedTexture.height != height)
                {
                    RenderTexture.ReleaseTemporary(_interlacedTexture);
                    _interlacedTexture = null;
                }
            }

			if (_interlacedTexture == null)
            {
                _interlacedTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                _interlacedTexture.filterMode = FilterMode.Point;

                if (!_interlacedTexture.IsCreated())
                {
                    _interlacedTexture.Create();
                }
            }
		}

		private static bool HasAlphaChannel(DeckLinkPlugin.PixelFormat format)
		{
			bool result = false;
			switch (format)
			{
				case DeckLinkPlugin.PixelFormat.BGRA_8bpp_444:
				case DeckLinkPlugin.PixelFormat.ARGB_8bpp_444:
					result = true;
					break;
			}
			return result;
		}

		public static bool OutputFormatSupported(DeckLinkPlugin.PixelFormat format)
        {
			bool result = false;
            switch (format)
            {
				case DeckLinkPlugin.PixelFormat.YCbCr_10bpp_422:
				case DeckLinkPlugin.PixelFormat.YCbCr_8bpp_422:
                case DeckLinkPlugin.PixelFormat.BGRA_8bpp_444:
                case DeckLinkPlugin.PixelFormat.ARGB_8bpp_444:
				case DeckLinkPlugin.PixelFormat.RGBX_10bpp_444:
				case DeckLinkPlugin.PixelFormat.RGBX_10bpp_444_LE:
				case DeckLinkPlugin.PixelFormat.RGB_10bpp_444:
                    result = true;
					break;
            }
			return result;
        }

		public int TargetFramerate
		{
			get { return _targetFrameRate; }
		}

		public int OutputFramerate
		{
			get
			{
				if (DeckLinkSettings.Instance._multiOutput)
				{
					return _outputFrameRate;
				}
				else
				{
					return Application.targetFrameRate;
				}
			}
		}

		public void SetCamera(Camera camera)
		{
			if (_camera != null)
			{
				_camera.targetTexture = null;
			}

			_camera = camera;

			if(camera != null)
			{
				camera.targetTexture = _inputTexture;
			}
		}

		public void SetRightEyeCamera(Camera camera)
		{
			if(_rightEyeCamera != null)
			{
				_rightEyeCamera.targetTexture = null;
			}

			_rightEyeCamera = camera;

			if(camera != null)
			{
				camera.targetTexture = _rightInputTexture;
			}
		}

		public bool CanOutputFrame()
		{
			if (Time.frameCount != _currFrame)
			{
				_currFrame = Time.frameCount;

				if(_syncedToInput != null)
				{
					_canOutputFrame = _syncedToInput.Device != null && _syncedToInput.Device.IsStreamingInput && _syncedToInput.Device.InputFrameReceived();
				}
				else
				{
					float secondsPerFrame = 1f / (float)_outputFrameRate;
					float delta = Mathf.Min(secondsPerFrame, Time.unscaledDeltaTime);

					_timeSinceLastFrame += delta;

					if (_outputFrameRate < 0 || _timeSinceLastFrame >= secondsPerFrame)
					{
						if (secondsPerFrame > 0)
						{
							_timeSinceLastFrame = _timeSinceLastFrame % secondsPerFrame;
							_canOutputFrame = true;
						}
						else
						{
							_timeSinceLastFrame = 0;
							_canOutputFrame = true;
						}
					}
					else
					{
						_canOutputFrame = false;
					}
				}
			}

			return _canOutputFrame;
		}

		private void RegisterAudioOutput()
		{
			if (_audioOutputManager != null)
			{
				_audioOutputManager.RegisterDevice(_device.DeviceIndex);
			}
		}

		private void AttachToCamera()
		{
			if (_camera != null)
			{
				_camera.targetTexture = _inputTexture;

				if(_rightEyeCamera != null)
				{
					_rightEyeCamera.targetTexture = _rightInputTexture;
				}
			}
			else if (gameObject.GetComponent<Camera>() != null)
			{
				_camera = gameObject.GetComponent<Camera>();
				_camera.targetTexture = _inputTexture;

				if (_rightEyeCamera != null)
				{
					_rightEyeCamera.targetTexture = _rightInputTexture;
				}
			}
		}

		protected override void BeginDevice()
		{
			_currCapturedFrame = 0;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			_device.GenlockOffset = _genlockPixelOffset;

			if (_current3DEnabled)
			{
				if (!DeckLinkPlugin.IsOutputMode3DSupported(_deviceIndex, _modeIndex))
				{
					_current3DEnabled = false;
					Debug.LogWarning("[AVProDeckLink] Output mode does not support stereo mode " + _modeIndex + ", disabling");
				}
			}

			DeckLinkPlugin.Set3DPlaybackEnabled(_device.DeviceIndex, _current3DEnabled);
			_device.LowLatencyMode = _lowLatencyMode;

			// Set keying mode before the output has started, as this can affect buffer allocation sizes
			if (_keyerMode != KeyerMode.None)
			{
				_device.CurrentKeyingMode = _keyerMode;
			}

			// Try starting output
			if (!_device.StartOutput(_modeIndex))
			{
				Debug.LogWarning("[AVProDeckLink] device failed to start.");
				StopOutput();
				_device = null;
			}
			else
			{
				DeviceMode mode = _device.GetOutputMode(_modeIndex);

				RegisterAudioOutput();
				float framerate = mode.FrameRate;

				InitCaptureBlendResources(mode.Width, mode.Height);

				if (mode.InterlacedFieldMode)
				{
					_interlaced = true;
					framerate *= 2;
					InitInterlaceResources(mode.Width, mode.Height);
				}
				else
				{
					_interlaced = false;
				}

				InitConversionResources(mode.PixelFormat, mode.Width, mode.Height);

				if (!DeckLinkSettings.Instance._multiOutput && !_syncedToInput)
				{
					Application.targetFrameRate = _targetFrameRate = Mathf.CeilToInt(framerate);
					#if !UNITY_2018_2_OR_NEWER
					Time.captureFramerate = _targetFrameRate;
					#endif
				}
				else
				{
					Application.targetFrameRate = Time.captureFramerate = -1;
					_outputFrameRate = _targetFrameRate = Mathf.CeilToInt(framerate);
				}

				AttachToCamera();
			}
#endif
		}
		
		private void UnregisterAudioOutput()
		{
			if (_audioOutputManager != null)
			{
				if (_device != null)
				{
					_audioOutputManager.UnregisterDevice(_device.DeviceIndex);
				}
			}
		}

		public bool StopOutput()
		{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			UnregisterAudioOutput();

			if (_device != null)
			{
				_device.StopOutput();
				_device = null;
			}

			_targetFrameRate = -1;
			if (DeckLinkManager.Instance != null)
			{
				_outputFrameRate = -1;
			}

			Application.targetFrameRate = Time.captureFramerate = -1;

			_interlaced = false;

			return true;
#else
            return false;
#endif
		}

		protected override void Cleanup()
		{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			StopOutput();

			if (_inputTexture != null)
			{
				RenderTexture.ReleaseTemporary(_inputTexture);
				_inputTexture = null;
			}

			if(_rightInputTexture != null)
			{
				RenderTexture.ReleaseTemporary(_rightInputTexture);
				_rightInputTexture = null;
			}

			if(_capturedFrames != null)
			{
				foreach(var frame in _capturedFrames)
				{
					RenderTexture.ReleaseTemporary(frame);
				}
				_capturedFrames = null;
			}

			if(_rightCapturedFrames != null)
			{
				foreach(var frame in _rightCapturedFrames)
				{
					RenderTexture.ReleaseTemporary(frame);
				}
				_rightCapturedFrames = null;
			}

			if(_blended != null)
			{
				RenderTexture.ReleaseTemporary(_blended);
				_blended = null;
			}

			if(_rightBlended != null)
			{
				RenderTexture.ReleaseTemporary(_rightBlended);
				_rightBlended = null;
			}

			if (_interlacedTexture != null)
			{
				RenderTexture.ReleaseTemporary(_interlacedTexture);
				_blended = null;
			}

			DeckLinkPlugin.SetOutputBufferPointer(_deviceIndex, null);
			DeckLinkPlugin.SetOutputTexturePointer(_deviceIndex, IntPtr.Zero);
			DeckLinkPlugin.SetRightOutputBufferPointer(_deviceIndex, null);
			DeckLinkPlugin.SetRightOutputTexturePointer(_deviceIndex, IntPtr.Zero);

			if(_convertedTexture != null)
			{
				RenderTexture.ReleaseTemporary(_convertedTexture);
				_convertedTexture = null;
				_convertedPointer = IntPtr.Zero;
			}

			if(_rightConvertedTexture)
			{
				RenderTexture.ReleaseTemporary(_rightConvertedTexture);
				_rightConvertedTexture = null;
				_rightConvertedPointer = IntPtr.Zero;
			}

			if (_outputBuffer != null)
			{
				_outputBuffer = null;
			}

			if(_rightOutputBuffer != null)
			{
				_rightOutputBuffer = null;
			}

			if (_parameters != null)
			{
				_parameters.Release();
				_parameters = null;
			}

			if (_convertedCompBuffer != null)
			{
				_convertedCompBuffer.Release();
				_convertedCompBuffer = null;
			}

			if(_interlaceMaterial != null)
			{
				Destroy(_interlaceMaterial);
				_interlaceMaterial = null;
			}

			if(_conversionMaterial != null)
			{
				Destroy(_conversionMaterial);
				_conversionMaterial = null;
			}

			if(_blendMat != null)
			{
				Destroy(_blendMat);
				_blendMat = null;
			}
#endif
		}

		private void Convert(Texture inputTexture, RenderTexture convertedTexture, byte[] outputBuffer)
		{
			if(convertedTexture != null)
			{
				if (_conversionMaterial != null)
				{
					Graphics.Blit(inputTexture, convertedTexture, _conversionMaterial);
				}
				else
				{
					Graphics.Blit(inputTexture, convertedTexture);
				}
			}
			else if(_convertedCompBuffer != null)
			{
				if (_abgrTo10bitARGB == null)
				{
					Debug.LogError("[AVProDeckLink] Unable to find shader to covert ABGR to 10bit RGBA");
					return;
				}
				int kernelHandle = _abgrTo10bitARGB.FindKernel("RGBA_to_10RGBX");
				_abgrTo10bitARGB.SetTexture(kernelHandle, "input", inputTexture);
				_abgrTo10bitARGB.SetBuffer(kernelHandle, "result", _convertedCompBuffer);
				_abgrTo10bitARGB.SetBuffer(kernelHandle, "constBuffer", _parameters);
				_abgrTo10bitARGB.Dispatch(kernelHandle, inputTexture.width / 8, inputTexture.height / 8, 1);

				_convertedCompBuffer.GetData(outputBuffer);
			}
			else
			{
				Debug.Log("[AVPro DeckLink] Something really wrong happened, this path shouldn't be possible");
			}
		}

		private void CaptureFrame()
		{
			if(_camera == null)
			{
				if(_noCameraMode == NoCameraMode.Colour)
				{
					var curr = RenderTexture.active;
					Graphics.SetRenderTarget(_inputTexture);
					GL.Clear(true, true, _defaultColour);
					Graphics.SetRenderTarget(curr);
				}
				else if(_noCameraMode == NoCameraMode.DefaultTexture)
				{
					Graphics.Blit(_defaultTexture != null ? _defaultTexture : Texture2D.blackTexture, _inputTexture);
				}
			}

			if (_rightEyeCamera == null && _rightInputTexture != null)
			{
				if (_noCameraMode == NoCameraMode.Colour)
				{
					var curr = RenderTexture.active;
					Graphics.SetRenderTarget(_rightInputTexture);
					GL.Clear(true, true, _defaultColour);
					Graphics.SetRenderTarget(curr);
				}
				else if (_noCameraMode == NoCameraMode.DefaultTexture)
				{
					Graphics.Blit(_defaultTexture != null ? _defaultTexture : Texture2D.blackTexture, _rightInputTexture);
				}
			}


			if (_capturedFrames != null)
			{
				_capturedFrames[_currCapturedFrame].DiscardContents();
				Graphics.Blit(_inputTexture, _capturedFrames[_currCapturedFrame]);

				if (_current3DEnabled)
				{
					_rightCapturedFrames[_currCapturedFrame].DiscardContents();
					Graphics.Blit(_rightInputTexture, _rightCapturedFrames[_currCapturedFrame]);
				}

				prevFrameTime = currFrameTime;
				currFrameTime = Time.unscaledTime;

				_currCapturedFrame = (byte)((_currCapturedFrame + 1) % 2);
			}
			else
			{
				_blended.DiscardContents();
				Graphics.Blit(_inputTexture, _blended);
				if (_current3DEnabled)
				{
					Graphics.Blit(_rightInputTexture, _rightBlended);
				}
			}
		}

		private void ProcessAudio()
		{
			if (_audioOutputManager)
			{
				if (_muteOutputAudio)
				{
					_audioOutputManager.UnregisterDevice(_deviceIndex);
				}
				else
				{
					_audioOutputManager.RegisterDevice(_deviceIndex);
				}
			}
		}

		private void BlendCapturedFrames()
		{
			float timeSinceLastRenderedFrame = currFrameTime - prevFrameTime;

			float t = 1f - (timeSinceLastRenderedFrame == 0f ? 1f : _timeSinceLastFrame / timeSinceLastRenderedFrame);
			t = Mathf.Clamp01(t);

			_blendMat.SetFloat("_t", t);

			uint currTex = (_currCapturedFrame + 1U) % 2U;
			_blendMat.SetTexture("_AfterTex", _capturedFrames[currTex]);
			Graphics.Blit(_capturedFrames[_currCapturedFrame], _blended, _blendMat);
			if(_current3DEnabled)
			{
				_blendMat.SetTexture("_AfterTex", _rightCapturedFrames[currTex]);
				Graphics.Blit(_rightCapturedFrames[_currCapturedFrame], _rightBlended, _blendMat);
			}
		}

		private RenderTexture Interlace(RenderTexture inputTexture, bool forwardPass)
		{
			if (_interlaced)
			{
				if(_interlacedTexture == null || _interlaceMaterial == null)
				{
					Debug.LogError("[AVPro DeckLink] Something went really wrong, I should not be here :(");
				}

				Graphics.Blit(inputTexture, _interlacedTexture, _interlaceMaterial, _interlacePass);
				// Notify the plugin that the interlaced frame is complete now
				DeckLinkPlugin.SetInterlacedOutputFrameReady(_device.DeviceIndex, _interlacePass == 1);

				if(forwardPass)
				{
					_interlacePass = (_interlacePass + 1) % 2;
				}

				return _interlacedTexture;
			}

			return inputTexture;
		}

		private void AdjustPlaybackFramerate()
		{
			int numWaitingOutputFrames = DeckLinkPlugin.GetOutputBufferedFramesCount(_device.DeviceIndex);
			
			// Dynamically adjust frame rate so we get a smooth output
			int target = _targetFrameRate;

			if (numWaitingOutputFrames < _bufferBalance)
			{
				target = Mathf.CeilToInt(_targetFrameRate + 1);
			}
			else if (numWaitingOutputFrames > _bufferBalance)
			{
				target = Mathf.CeilToInt(_targetFrameRate - 1);
			}
			else
			{
				target = _targetFrameRate;
			}

			if (!DeckLinkSettings.Instance._multiOutput)
			{
				Application.targetFrameRate = target;
				#if !UNITY_2018_2_OR_NEWER
				Time.captureFramerate = target;
				#endif
			}
			else
			{
				_outputFrameRate = target;
			}
		}

		protected override void Process()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			if (_device == null)
            {
                return;
            }

			bool restartDevice = false;

			if(_current3DEnabled != _enable3D)
			{
				_current3DEnabled = _enable3D;
				restartDevice = true;
			}

			_device.LowLatencyMode = _lowLatencyMode;

			// If we're syncing to an input, then make sure we output using the same frame rate
			if (_syncedToInput != null && _syncedToInput.Device != null && _syncedToInput.Device.IsStreamingInput && _syncedToInput.ModeIndex >= 0)
			{
				// If we're using auto detect mode then we need to wait for frames to start coming in, otherwise
				// it may be still on the default mode awaiting auto detection
				if (!_syncedToInput._autoDetectMode || _syncedToInput.Device.FramesTotal > 1)
				{
					var inputMode = _syncedToInput.Device.CurrentMode;
					var outputMode = (Device == null) ? null : Device.CurrentOutputMode;

					// TODO: add better support for interlaced modes
					float inputFrameRate = inputMode.FrameRate;// * (inputMode.InterlacedFieldMode ? 2 : 1);

					if (outputMode == null || outputMode.FrameRate != inputFrameRate)
					{
						_filterModeByFPS = true;
						_filterModeByInterlacing = true;

						_modeFPS = inputFrameRate;
						_modeInterlacing = false;

						restartDevice = true;
					}
				}
				else
				{
					return;
				}
			}

			if (restartDevice)
			{
				StopOutput();
				Begin(true);

				if(_device == null)
				{
					return;
				}
			}

			_enable3D = _current3DEnabled;

			if (_conversionMaterial != null)
			{
				//in this case, since we are dealing with non-srgb texture, need to do conversion from gamma to linear
				if (QualitySettings.activeColorSpace == ColorSpace.Linear && !_bypassGamma)
				{
					_conversionMaterial.EnableKeyword("APPLY_GAMMA");
				}
				else
				{
					_conversionMaterial.DisableKeyword("APPLY_GAMMA");
				}
				if (_ignoreAlphaChannel)
				{
					_conversionMaterial.EnableKeyword(ShaderKeyIgnoreAlpha);
				}
				else
				{
					_conversionMaterial.DisableKeyword(ShaderKeyIgnoreAlpha);
				}

				switch (_colorspaceMode)
				{
					case DeckLink.ColorspaceMode.Rec709:
	        			_conversionMaterial.DisableKeyword(ShaderKeyRec2020);
    		    		_conversionMaterial.DisableKeyword(ShaderKeyRec2100);
						_conversionMaterial.EnableKeyword(ShaderKeyRec709);
						break;
					case DeckLink.ColorspaceMode.Rec2020:
		        		_conversionMaterial.DisableKeyword(ShaderKeyRec709);
        				_conversionMaterial.DisableKeyword(ShaderKeyRec2100);
						_conversionMaterial.EnableKeyword(ShaderKeyRec2020);
						break;
					case DeckLink.ColorspaceMode.Rec2100:
				        _conversionMaterial.DisableKeyword(ShaderKeyRec709);
				        _conversionMaterial.DisableKeyword(ShaderKeyRec2020);
						_conversionMaterial.EnableKeyword(ShaderKeyRec2100);
						break;
				}
			}

			if (_convertedTexture != null && _convertedPointer == IntPtr.Zero)
			{
				_convertedPointer = _convertedTexture.GetNativeTexturePtr();
				DeckLinkPlugin.SetOutputTexturePointer(_deviceIndex, _convertedPointer);
			}

			if(_rightConvertedTexture != null && _rightConvertedPointer == IntPtr.Zero)
			{
				_rightConvertedPointer = _rightConvertedTexture.GetNativeTexturePtr();
				DeckLinkPlugin.SetRightOutputTexturePointer(_deviceIndex, _rightConvertedPointer);
			}

            if (_device.IsStreamingOutput)
            {
				CaptureFrame();

				if (CanOutputFrame())
				{
					if(_syncedToInput)
					{
						_syncedToInput.UnsetInputReceivedFlag = true;
					}

					if (DeckLinkSettings.Instance._multiOutput)
					{
						BlendCapturedFrames();
					}

					RenderTexture input = _blended;

					input = Interlace(input, !_current3DEnabled);
					Convert(input, _convertedTexture, _outputBuffer);

					if(_current3DEnabled)
					{
						input = _rightBlended;
						input = Interlace(input, true);
						Convert(input, _rightConvertedTexture, _rightOutputBuffer);
					}

					if(_syncedToInput == null)
					{
						AdjustPlaybackFramerate();
					}

					DeckLinkPlugin.SetDeviceOutputReady(_deviceIndex);
				}

			}
			ProcessAudio();
#endif
        }

        protected override bool IsInput()
        {
            return false;
        }

        public override void OnDestroy()
        {
            refCount--;

            if(refCount == 0)
            {
                QualitySettings.vSyncCount = prevRefCount;
            }

			if (_convertedCompBuffer != null)
			{
				_convertedCompBuffer.Release();
				_convertedCompBuffer = null;
			}

			if (_parameters != null)
			{
				_parameters.Release();
				_parameters = null;
			}

			base.OnDestroy();
        }

#if UNITY_EDITOR
        [ContextMenu("Save Output PNG")]
        public override void SavePNG()
        {
			if (_rightInputTexture != null)
			{
				Helper.SavePNG("Image-Output-Left.png", _inputTexture);
				Helper.SavePNG("Image-Output-Right.png", _rightInputTexture);
				Helper.SavePNG("Image-Output-Converted-Left.png", _convertedTexture);
				Helper.SavePNG("Image-Output-Converted-Right.png", _rightConvertedTexture);
			}
			else
			{
				Helper.SavePNG("Image-Output.png", _inputTexture);
				Helper.SavePNG("Image-Output-Converted.png", _convertedTexture);
			}
        }

#if UNITY_5_6_OR_NEWER
		[ContextMenu("Save Output EXR")]
		public override void SaveEXR()
		{
			if (_rightInputTexture != null)
			{
				Helper.SaveEXR("Image-Output-Left.exr", _inputTexture);
				Helper.SaveEXR("Image-Output-Right.exr", _rightInputTexture);
				Helper.SaveEXR("Image-Output-Converted-Left.exr", _convertedTexture);
				Helper.SaveEXR("Image-Output-Converted-Right.exr", _rightConvertedTexture);
			}
			else
			{
				Helper.SaveEXR("Image-Output.exr", _inputTexture);
				Helper.SaveEXR("Image-Output-Converted.exr", _convertedTexture);
			}
		}
#endif
#endif

	}
}
