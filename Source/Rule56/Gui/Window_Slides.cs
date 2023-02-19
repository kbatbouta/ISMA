using System.Collections.Generic;
using UnityEngine;
using Verse;
namespace CombatAI.Gui
{
    public class Window_Slides : Window
    {
        private int             curIndex;
        private bool[]          read;
        private List<HyperText> pages = new List<HyperText>();
        
        public Window_Slides(HyperTextDef[] defs, bool forcePause = true, bool skippable = true)
        {
            this.read  = new bool[defs.Length];
            this.pages = new List<HyperText>(defs.Length);
            foreach (HyperTextDef def in defs)
            {
                this.pages.Add(HyperTextMaker.Make(def));
            }
            this.doCloseX   = skippable;
            this.forcePause = forcePause;
            this.draggable  = false;
        }

        public override Vector2 InitialSize
        {
            get => new Vector2(800, 600);
        }

        public override void DoWindowContents(Rect inRect)
        {
            HyperText page = pages[curIndex];
            page.Draw(inRect.TopPartPixels(inRect.height - 50));
            bool ButtonText(Rect rect, string text, Color? color)
            {
                bool result = false;
                Gui.GUIUtility.ExecuteSafeGUIAction(() =>
                {
                    if (color != null)
                    {
                        GUI.color = color.Value;
                    }
                    result = Widgets.ButtonText(rect, text);
                });
                return result;
            }
            Rect counterRect = inRect.BottomPartPixels(50).TopPartPixels(20);
            Gui.GUIUtility.ExecuteSafeGUIAction(() =>
            {
                GUIFont.Font   = GUIFontSize.Smaller;
                GUIFont.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(counterRect, $"{curIndex + 1} / {pages.Count}");
            });
            if (curIndex != 0)
            {
                Rect buttonRect = inRect.BottomPartPixels(30);
                buttonRect       = buttonRect.ContractedBy(3);
                buttonRect.width = 310;                    
                buttonRect       = buttonRect.CenteredOnXIn(inRect);
                if (curIndex < pages.Count - 1)
                {
                    if (ButtonText(buttonRect.RightPartPixels(150), $"Next page >", null))
                    {
                        curIndex++;
                    }
                }
                else
                {
                    if (ButtonText(buttonRect.RightPartPixels(150), R.Keyed.CombatAI_Close, Color.green))
                    {
                        Close();
                    }
                }
                if (curIndex > 0 && ButtonText(buttonRect.LeftPartPixels(150), $"< Previous page", null))
                {
                    curIndex--;
                }
            }
            else
            {                
                Rect buttonRect = inRect.BottomPartPixels(30);
                buttonRect       = buttonRect.ContractedBy(3);
                buttonRect.width = 150;                    
                buttonRect       = buttonRect.CenteredOnXIn(inRect);
                if (pages.Count != 0)
                {
                    if (ButtonText(buttonRect, $"Next page >", null))
                    {
                        curIndex++;
                    }
                }
                else
                {
                    if (ButtonText(buttonRect, R.Keyed.CombatAI_Close, Color.green))
                    {
                        Close();
                    }
                }
            }
        }
    }
}
