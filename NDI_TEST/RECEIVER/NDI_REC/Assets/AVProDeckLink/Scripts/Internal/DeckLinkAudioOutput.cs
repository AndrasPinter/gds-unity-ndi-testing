using UnityEngine;
using System.Collections.Generic;
using System.Threading;

//-----------------------------------------------------------------------------
// Copyright 2014-2018 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink
{
    public class DeckLinkAudioOutput : MonoBehaviour
    {
        private List<int> _registeredDevices;
		private Mutex _mutex;

        void Awake()
        {
            _registeredDevices = new List<int>();
			_mutex = new Mutex();
		}

        // Use this for initialization
        void Start()
        {
            
        }

        // Update is called once per frame
        void Update()
        {
        }

        public void RegisterDevice(int deviceIndex)
        {
			_mutex.WaitOne();
            if (!_registeredDevices.Contains(deviceIndex))
            {
			    _registeredDevices.Add(deviceIndex);
            }
            //Debug.Log("devices: " + _registeredDevices.Count);
			_mutex.ReleaseMutex();
		}

        public void UnregisterDevice(int deviceIndex)
        {
			_mutex.WaitOne();
            if (_registeredDevices.Contains(deviceIndex))
            {
			    _registeredDevices.Remove(deviceIndex);
            }
            //Debug.Log("unreg: " + _registeredDevices.Count);
			_mutex.ReleaseMutex();
		}

        public void OnAudioFilterRead(float[] data, int channels)
        {
            DeckLinkManager manager = DeckLinkManager.Instance;
            if(manager == null)
            {
                return;
            }

			_mutex.WaitOne();
			foreach (var deviceIndex in _registeredDevices)
            {
                var device = manager.GetDevice(deviceIndex);

                if (device == null)
                {
					_mutex.ReleaseMutex();

					return;
                }

                short[] buffer = new short[data.Length];

                for (int i = 0; i < data.Length; ++i)
                {
                    buffer[i] = (short)(data[i] * 32767f);
                }

                DeckLinkPlugin.OutputAudio(deviceIndex, buffer, buffer.Length * 2);
            }
			_mutex.ReleaseMutex();
		}
    }
}

