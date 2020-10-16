using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DeckLinkInput))]
    public class DeckLinkInputEditor : DeckLinkEditor
    {
        private DeckLinkInput _camera;
		protected SerializedProperty _flipx;
		protected SerializedProperty _flipy;
        protected SerializedProperty _propInputBufferCount;
        protected SerializedProperty _propInputReadBufferCount;

		void OnEnable()
        {
            LoadSettings();
            Init();
        }

        private void Init()
        {
            _camera = (this.target) as DeckLinkInput;

            _selectedDevice = serializedObject.FindProperty("_deviceIndex");

            _selectedMode = serializedObject.FindProperty("_modeIndex"); ;

            _selectedResolution = serializedObject.FindProperty("_resolutionIndex");

            _exactDeviceName = serializedObject.FindProperty("_exactDeviceName");
            _desiredDeviceName = serializedObject.FindProperty("_desiredDeviceName");
            _desiredDeviceIndex = serializedObject.FindProperty("_desiredDeviceIndex");
            _exactDeviceIndex = serializedObject.FindProperty("_exactDeviceIndex");
            _filterDeviceByName = serializedObject.FindProperty("_filterDeviceByName");
            _filterDeviceByIndex = serializedObject.FindProperty("_filterDeviceByIndex");

            _filterModeByResolution = serializedObject.FindProperty("_filterModeByResolution");
            _filterModeByFormat = serializedObject.FindProperty("_filterModeByFormat");
            _filterModeByFPS = serializedObject.FindProperty("_filterModeByFPS");
            _filterModeByInterlacing = serializedObject.FindProperty("_filterModeByInterlacing");
            _modeWidth = serializedObject.FindProperty("_modeWidth");
            _modeHeight = serializedObject.FindProperty("_modeHeight");
            _modeFormat = serializedObject.FindProperty("_modeFormat");
            _modeFPS = serializedObject.FindProperty("_modeFPS");
            _modeInterlacing = serializedObject.FindProperty("_modeInterlacing");

            _showExplorer = serializedObject.FindProperty("_showExplorer");

			_propInputBufferCount = serializedObject.FindProperty("_inputBufferCount");
			_propInputReadBufferCount = serializedObject.FindProperty("_inputBufferReadCount");

			_flipx = serializedObject.FindProperty("_flipX");
			_flipy = serializedObject.FindProperty("_flipY");

			_isInput = true;
        }

        protected override bool ModeValid(DeckLinkPlugin.PixelFormat format)
        {
            return FormatConverter.InputFormatSupported(format);
        }

		private void DrawFlipCheckboxes()
		{
			_flipx.boolValue = EditorGUILayout.Toggle("Flip X", _flipx.boolValue);
			_camera.FlipX = _flipx.boolValue;

			_flipy.boolValue = EditorGUILayout.Toggle("Flip Y", _flipy.boolValue);
			_camera.FlipY = _flipy.boolValue;
		}

        private void DrawBufferStats()
        {
            if (_camera.Device == null || !_camera.Device.IsStreamingInput)
            {
                EditorGUILayout.PropertyField(_propInputBufferCount);
                EditorGUILayout.PropertyField(_propInputReadBufferCount);
                _propInputReadBufferCount.intValue = Mathf.Clamp(_propInputReadBufferCount.intValue, 0, _propInputBufferCount.intValue - 1);
            }
            else if (_camera.Device != null && _camera.Device.IsStreamingInput)
            {
                int totalBufferCount;
                int readBufferCount;
                int usedBufferCount;
                int pendingBufferCount;
                if (_camera.Device.GetInputBufferStats(out totalBufferCount, out readBufferCount, out usedBufferCount, out pendingBufferCount))
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    EditorGUILayout.LabelField(_propInputBufferCount.displayName, totalBufferCount.ToString());
                    EditorGUILayout.LabelField(_propInputReadBufferCount.displayName, readBufferCount.ToString());
                    GUILayout.Label("Used: " + usedBufferCount + " Pending: " + pendingBufferCount.ToString());
                    GUILayout.EndVertical();
                }
            }
        }

        public override void OnInspectorGUI()
        {
			if(serializedObject == null)
			{
				return;
			}

            if (_camera == null)
            {
                Init();
            }

            serializedObject.Update();

            if (!Application.isPlaying)
            {
                DrawDefaultInspector();
                DrawBufferStats();
				DrawFlipCheckboxes();

				EditorGUILayout.PropertyField(_showExplorer);

                EditorGUIUtility.labelWidth = 150;
                DrawDeviceFilters();
                EditorGUIUtility.labelWidth = 175;
                DrawModeFilters(true);
                DrawPreviewTexture(null);
                OnInspectorGUI_About();
            }
            else
            {
                DrawDefaultInspector();
                DrawBufferStats();
				DrawFlipCheckboxes();

				EditorGUILayout.PropertyField(_showExplorer);
                DrawPreviewTexture(_camera);

                OnInspectorGUI_About();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
