using UnityEngine;
using System.Collections;

//-----------------------------------------------------------------------------
// Copyright 2015-2018 RenderHeads Ltd.  All rights reserverd.
//-----------------------------------------------------------------------------

namespace RenderHeads.Media.AVProDeckLink.Demos
{
    public class UGUIDemo : MonoBehaviour
	{
        public GameObject display;
        public DeckLink decklink;

        private int currRot = 0;

        public void RotateDisplay()
        {
            if(display != null)
            {
                currRot = (currRot + 90) % 360;
                display.transform.rotation = Quaternion.AngleAxis((float)currRot, Vector3.forward);
            }
        }

        public void ToggleExplorer()
        {
            if (decklink != null)
            {
                decklink._showExplorer = !decklink._showExplorer;
            }
        }
    }
}
