using UnityEngine;
using System.Collections;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink
{
    [AddComponentMenu("AVPro DeckLink/ApplyToMaterial")]
    public class ApplyToMaterial : MonoBehaviour
    {
        public DeckLinkInput _inputDecklink;
		public Eye _eye = Eye.Left;
        public Material _material;
        public string _textureName;
		public string _rightTextureName = "_RightEyeTex";
        public Texture2D _defaultTexture;
        private static Texture2D _blackTexture;
        private static int _propEyeMode;

        public void SetInputDeckLink(DeckLinkInput decklink)
        {
            _inputDecklink = decklink;
            _inputDecklink.Begin();
            Update();
        }

        void Start()
        {
            if (_defaultTexture == null)
            {
                _defaultTexture = _blackTexture;
            }

            Update();
        }

        void Update()
        {
            if (_inputDecklink != null)
            {
                if (_inputDecklink.OutputTexture != null)
                {
                    Apply(_inputDecklink.OutputTexture, _inputDecklink.RightOutputTexture);
                }
                else
                {
                    Apply(_defaultTexture, _defaultTexture);
                }
            }
        }

        private void Apply(Texture texture, Texture rightTexture)
        {
            if (_material != null)
            {
                if (string.IsNullOrEmpty(_textureName))
                    _material.mainTexture = texture;
                else
                    _material.SetTexture(_textureName, texture);

				_material.SetTexture(_rightTextureName, texture);
				_material.SetInt(_propEyeMode, (int)_eye);
            }
        }

        void OnDestroy()
        {
            Apply(null, null);

            _defaultTexture = null;

            if (_blackTexture != null)
            {
                Texture2D.Destroy(_blackTexture);
                _blackTexture = null;
            }
        }

        void Awake()
        {
            if (_blackTexture == null)
            {
                CreateTexture();
            }
            if (_propEyeMode == 0)
            {
                _propEyeMode = Shader.PropertyToID("_EyeMode");
            }            
        }

        private static void CreateTexture()
        {
            _blackTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false, false);
            _blackTexture.name = "AVProDeckLink-BlackTexture";
            _blackTexture.filterMode = FilterMode.Point;
            _blackTexture.wrapMode = TextureWrapMode.Clamp;
            _blackTexture.SetPixel(0, 0, Color.black);
            _blackTexture.Apply(false, true);
        }
    }
}