using UnityEngine;
using System.Collections.Generic;
using System.Collections;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink.Demos
{
    public class InputExplorerDemo : MonoBehaviour
    {
        class Instance
        {
            public Vector2 scrollPos;
            public DeckLinkInput decklink;
            public bool showModes = false;
        }

        public GUISkin _guiSkin;
        public bool _autoDetect;
		public bool _logDeviceModes;

        private List<Instance> _instances = new List<Instance>();
        private Vector2 _horizScrollPos = Vector2.zero;

        private Texture _zoomed = null;
        private const float ZoomTime = 0.25f;
        private float _zoomTimer;
        private bool _zoomUp;
        private Rect _zoomSrcDest;

        public void Start()
        {
            Application.runInBackground = true;       
            EnumerateDevices();
        }

        public void EnumerateDevices()
        {
            foreach(var instance in _instances)
            {              
                Destroy(instance.decklink.gameObject);
            }
            _instances.Clear();

            // Enumerate all devices
            int numDevices = DeckLink.GetNumDevices();
			if (_logDeviceModes)
			{
				print("num devices: " + numDevices);
			}
            for (int i = 0; i < numDevices; i++)
            {
				Device device = DeckLink.GetDevice(i);
				if(device.NumInputModes == 0)
				{
					continue;
				}

                GameObject decklinkObject = new GameObject();
                DeckLinkInput input = decklinkObject.AddComponent<DeckLinkInput>();
                input.DeviceIndex = i;
                input.ModeIndex = 0;
                input._playOnStart = true;
                input._autoDeinterlace = true;
                input._autoDetectMode = _autoDetect;
                input.Begin();

                Instance instance = new Instance()
                {
                    decklink = input
                };
                _instances.Add(instance);

				if (_logDeviceModes)
				{
					// Enumerate input modes
					print("device " + i + ": " + input.Device.Name + " has " + input.Device.NumInputModes + " input modes");
					for (int j = 0; j < input.Device.NumInputModes; j++)
					{
						DeviceMode mode = input.Device.GetInputMode(j);
						print("  mode " + j + ": " + mode.Width + "x" + mode.Height + " @" + mode.FrameRate.ToString("F2") + "fps [" + mode.PixelFormatDescription + "] idx:" + mode.Index);
					}
				}
            }
        }

        public void Update()
        {
            // Handle mouse click to unzoom
            if (_zoomed != null)
            {
                if (_zoomUp)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        _zoomTimer = ZoomTime;
                        _zoomUp = false;
                    }
                    else
                    {
                        _zoomTimer += Time.deltaTime;
                    }

                }
                else
                {
                    if (_zoomTimer <= 0.0f)
                    {
                        _zoomed = null;
                    }
                    _zoomTimer -= Time.deltaTime;
                }
            }
        }

        public void OnGUI()
        {
            GUI.skin = _guiSkin;

            _horizScrollPos = GUILayout.BeginScrollView(_horizScrollPos, false, false);
            GUILayout.BeginHorizontal();

            for (int i = 0; i < _instances.Count; i++)
            {
                DeckLinkInput decklink = _instances[i].decklink;

                GUILayout.BeginVertical("box", GUILayout.MaxWidth(375));

                // Image preview
                Rect cameraRect = GUILayoutUtility.GetRect(375, 200);
                if (GUI.Button(cameraRect,  decklink.OutputTexture))
                {
                    if (_zoomed == null)
                    {
                        _zoomed =  decklink.OutputTexture;
                        _zoomSrcDest = cameraRect;
                        _zoomUp = true;
                    }
                }

                // Controls
                GUILayout.Box("Device " + i + ": " + decklink.Device.Name);
                if (!decklink.Device.IsStreaming)
                {
                    GUILayout.Box("Stopped");
                }
                else
                {
                    GUILayout.Box(string.Format("{0} [{1}]",decklink.Device.CurrentMode.ModeDescription, decklink.Device.CurrentMode.PixelFormatDescription));
					GUILayout.BeginHorizontal();
					if (decklink.FlipX)
					{
						GUI.color = Color.green;
					}

					if (GUILayout.Button("Flip X"))
					{
						decklink.FlipX = !decklink.FlipX;
					}

					GUI.color = Color.white;

					if (decklink.FlipY)
					{
						GUI.color = Color.green;
					}

					if (GUILayout.Button("Flip Y"))
					{
						decklink.FlipY = !decklink.FlipY;
					}

					GUI.color = Color.white;

					GUILayout.EndHorizontal();
					if (!DeckLinkPlugin.IsNoInputSignal(decklink.Device.DeviceIndex))
                    {
                        GUILayout.Box(string.Format("Capture {0}hz Display {1}hz", decklink.Device.CurrentMode.FrameRate.ToString("F2"), decklink.Device.FPS.ToString("F2")));
                    }
                    else
                    {
                        GUILayout.Box("No Signal");
                    }
                    if (GUILayout.Button("Stop"))
                    {
                        if (_zoomed == null)
                        {
                            decklink.Device.StopInput();
                        }
                        _instances[i].showModes = true;
                    }
                }


                if (decklink.Device.AutoDeinterlace != GUILayout.Toggle(decklink.Device.AutoDeinterlace, "Auto Deinterlace", GUILayout.ExpandWidth(true)))
                {
                    decklink.Device.AutoDeinterlace = !decklink.Device.AutoDeinterlace;
                }

                GUILayout.Space(16f);

                _instances[i].showModes = GUILayout.Toggle(_instances[i].showModes, "Show Modes:");

                if (_instances[i].showModes)
                {
                    _instances[i].scrollPos = GUILayout.BeginScrollView(_instances[i].scrollPos, false, false);
                    for (int j = 0; j < decklink.Device.NumInputModes; j++)
                    {
                        DeviceMode mode = decklink.Device.GetInputMode(j);

                        GUI.color = Color.white;
                        if (decklink.Device.IsStreaming && decklink.Device.CurrentMode == mode)
                        {
                            GUI.color = Color.green;
                        }

                        if (GUILayout.Button(j + "/ " + mode.ModeDescription + " [" + mode.PixelFormatDescription + "]"))
                        {
                            if (_zoomed == null)
                            {
                                // Start selected device
                                decklink._modeIndex = j;
                                decklink.Begin();
                                _instances[i].showModes = false;
                            }
                        }
                    }
                    GUILayout.EndScrollView();
                }


                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            // Show zoomed camera image
            if (_zoomed != null)
            {
                Rect fullScreenRect = new Rect(0, 0, Screen.width, Screen.height);

                float t = Mathf.Clamp01(_zoomTimer / ZoomTime);
                t = Mathf.SmoothStep(0, 1, t);
                Rect r = new Rect();
                r.x = Mathf.Lerp(_zoomSrcDest.x, fullScreenRect.x, t);
                r.y = Mathf.Lerp(_zoomSrcDest.y, fullScreenRect.y, t);
                r.width = Mathf.Lerp(_zoomSrcDest.width, fullScreenRect.width, t);
                r.height = Mathf.Lerp(_zoomSrcDest.height, fullScreenRect.height, t);
                GUI.DrawTexture(r, _zoomed, ScaleMode.ScaleToFit, false);
            }
        }
    }
}