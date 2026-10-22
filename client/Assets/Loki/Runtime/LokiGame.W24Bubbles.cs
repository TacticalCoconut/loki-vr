using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 24 (w24): the Quick Use Bubbles and the tutorial; their opacity. the design's list of 2026-10-12.
    ///   Item 7   "The quick use spheres are occluding the tutorial panel." WHILE THE TUTORIAL RUNS the bubbles are not drawn and
    ///            take no touch and no trigger (LokiGame.W20Bubbles.cs W20Blocker "the tutorial", the alpha 0), in every step BUT
    ///            their own lesson (the casting lesson "cast-bubbles") and their own page on the menu above; the moment the
    ///            tutorial ends they follow their own settings again. In their lesson the guide stands at wave 23's side place
    ///            (LokiGame.W23Place.cs: "cast-bubbles").
    ///   Item 11  "the spheres are far too transparent when you look up at them ... Provide a slider that controls how transparent
    ///            they are ... And also Provide a slider that controls each individual bubble's transparency." Two sliders on
    ///            the menu above, each after a row's own buttons with its value beside it ("Opacity 80 %"): for ALL bubbles on
    ///            the main page's row "Quick Use Bubbles" (after "Edit…"); for the CHOSEN bubble on the bubbles' page's row
    ///            "Active" (after Off / On). No row was added: the pages are no taller. Each <see cref="BubbleOpacityMin"/> to 1 in steps of
    ///            5 %; drawn solidity = the two multiplied (UI/QuickBubble.cs Solid). Defaults: all <see cref="BubbleOpacityDefault"/>
    ///            (FLAGGED: the orchestrator's 0.8 of solid), each 100 %. Kept with the bubbles' layout (the account's).
    ///            "Shown" keeps its default "When looked at" (the design's correction: "after the tutorial the default for the quick use
    ///            bubbles needs to be only when looked at"); only IN THEIR LESSON they are always drawn, at
    ///            <see cref="BubbleLessonOpacity"/> at least, whatever the sliders and "Shown" say (nothing is written).
    ///   Item 12  "The blue sky makes it very difficult to see the spheres. So during the tutorial, make them have a little bit
    ///            more contrast": in their lesson the bubbles wear the contrast look (UI/QuickBubble.cs Contrast: a dark edge
    ///            line, the glass and the body in the theme's gold). <see cref="BubbleContrastAlways"/> would make it the
    ///            everyday look (FLAGGED: the design specified the tutorial only).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The bubbles are hidden and unusable while the tutorial runs, but for their own lesson and page.</summary>
        public const bool BubbleTutorialHides = true;
        /// <summary>The general opacity's default and lowest value; the least a bubble is drawn at in the bubbles' lesson.</summary>
        public const float BubbleOpacityDefault = 0.8f, BubbleOpacityMin = 0.2f, BubbleLessonOpacity = BubbleOpacityDefault;
        /// <summary>The contrast look in the tutorial's bubbles lesson; everywhere (the everyday look).</summary>
        public const bool BubbleContrastInLesson = true, BubbleContrastAlways = false;

        /// <summary>The tutorial's bubbles lesson runs now.</summary>
        public bool W24BubblesLesson => W18TutRunning && W22LessonKey == "cast-bubbles";
        /// <summary>The tutorial hides the bubbles now (item 7).</summary>
        public bool W24BubblesHidden => BubbleTutorialHides && W18TutRunning && !W24BubblesLesson && !W20Editing;
        /// <summary>The general opacity as stored (a broken value: the default).</summary>
        public static float W24BubbleOpacityAll { get { float v = UserSettings.Prefs.bubbleOpacity; return v > 0f ? Mathf.Clamp(v, BubbleOpacityMin, 1f) : BubbleOpacityDefault; } }
        /// <summary>Bubble i's own factor as stored.</summary>
        public static float W24BubbleOpacityOwn(int i) { float v = UserSettings.BubblesNow[i].opacity; return v > 0f ? Mathf.Clamp(v, BubbleOpacityMin, 1f) : 1f; }
        /// <summary>How solid bubble i is drawn now: the general opacity x its own; in the bubbles' lesson the lesson's at least.</summary>
        public float W24BubbleSolid(int i)
        {
            float v = Mathf.Clamp(W24BubbleOpacityAll * W24BubbleOpacityOwn(i), 0.04f, 1f);
            return W24BubblesLesson ? Mathf.Max(v, BubbleLessonOpacity) : v;
        }
        /// <summary>The bubbles wear the contrast look now (item 12).</summary>
        public bool W24BubbleContrast => BubbleContrastAlways || (BubbleContrastInLesson && W24BubblesLesson);
        /// <summary>The look is on a bubble (within the angle from which a "When looked at" bubble is whole: BubbleLookFull).</summary>
        public bool W24LooksAtBubble()
        {
            var rig = _boot.Rig; if (rig == null || _w20 == null) return false;
            var gaze = rig.GazeRay;
            for (int i = 0; i < BubbleCount; i++)
            {
                if (!UserSettings.BubblesNow[i].on) continue;
                var c = W20Centre(i); float own = Mathf.Atan2(W20Radius(i), Mathf.Max(Vector3.Distance(gaze.origin, c), 1e-3f)) * Mathf.Rad2Deg;
                if (Vector3.Angle(gaze.direction, c - gaze.origin) <= own + BubbleLookFull) return true;
            }
            return false;
        }

        private static string W24Pct(float v) => Lang.F("Opacity {0}", Mathf.RoundToInt(v * 100f) + " %");
        /// <summary>Every bubble's opacity: a slider after "Edit…" on the main page's "Quick Use Bubbles" row (called by
        /// InitW20Bubbles right after that row is added).</summary>
        private void W24AddOpacityAll(OverheadMenu m)
        {
            _w25SliderAll = m.AddExtraSlider("opacityall", BubbleOpacityMin, 1f, 0.05f, () => W24BubbleOpacityAll, // (client wave 25, w25tut, item 6: kept, to lock it during the tutorial: LokiGame.W25Tut.cs)
                v => UserSettings.Prefs.bubbleOpacity = v, () => { UserSettings.Save(); Debug.Log($"[Bubbles] the bubbles' opacity {W24BubbleOpacityAll * 100f:0} %"); }, () => W24Pct(W24BubbleOpacityAll));
        }
        /// <summary>The chosen bubble's own opacity: a slider after the choices of "Active" on the bubbles' page (called by
        /// InitW20Bubbles right after that row is added).</summary>
        private void W24AddOpacityOwn(OverheadMenu m)
        {
            _w25SliderOwn = m.AddExtraSlider("opacity", BubbleOpacityMin, 1f, 0.05f, () => W24BubbleOpacityOwn(_w20Sel), // (client wave 25, item 6)
                v => UserSettings.BubblesForEdit()[_w20Sel].opacity = v, () => { UserSettings.Save(); Debug.Log($"[Bubbles] bubble {_w20Sel + 1}: its opacity {W24BubbleOpacityOwn(_w20Sel) * 100f:0} %"); }, () => W24Pct(W24BubbleOpacityOwn(_w20Sel)));
        }
    }
}
