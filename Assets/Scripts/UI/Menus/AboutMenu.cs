using Dummiesman.TextureLoading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class AboutMenu : UIMenu
{
    public override bool HasNavBar => false;

    private Texture2D creditsTexture;

    private float timeSinceEntry = 0.0f;
    private float scrollAmountPx = 0.0f;
    private Rect creditsDrawArea = new Rect(64.0f, 480.0f, 320.0f, 240.0f);

    public override void Activate()
    {
        base.Activate();
        timeSinceEntry = 0.0f;
        scrollAmountPx = 0.0f;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    public override void Deactivate()
    {
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    public override void Update()
    {
        base.Update();
        timeSinceEntry += Time.deltaTime;
        if (timeSinceEntry > 1.5f && creditsTexture != null)
        {
            scrollAmountPx = Mathf.Floor((timeSinceEntry - 1.5f) * 50.0f + 0.5f) % creditsTexture.height;
        }
    }

    public override void Draw()
    {
        base.Draw();
        if (creditsTexture != null)
        {
            // draw credits image
            var viewportPixelArea = creditsDrawArea;

            int texW = creditsTexture.width;
            int texH = creditsTexture.height;
            int scroll = Mathf.FloorToInt(scrollAmountPx);
            int viewH = Mathf.FloorToInt(viewportPixelArea.height);

            if (scroll + viewH <= texH)
            {
                // single blit: source rows [scroll, scroll + viewH)
                var drawRect = ToPixelCoordinates(MenuToWidgetCoords(new Rect(viewportPixelArea.x, viewportPixelArea.y, texW, viewH)));
                GUI.DrawTextureWithTexCoords(
                    drawRect,
                    creditsTexture,
                    new Rect(0.0f, 1.0f - (scroll + viewH) / (float)texH, 1.0f, viewH / (float)texH));
            }
            else
            {
                // bottom part of the texture
                int firstH = texH - scroll;
                Rect firstRect = ToPixelCoordinates(MenuToWidgetCoords(new Rect(viewportPixelArea.x, viewportPixelArea.y, texW, firstH)));

                GUI.DrawTextureWithTexCoords(
                    firstRect,
                    creditsTexture,
                    new Rect(0.0f, 0.0f, 1.0f, firstH / (float)texH));

                // wrapped part from the top of the texture
                int secondH = viewH - firstH;
                Rect secondRect = ToPixelCoordinates(MenuToWidgetCoords(new Rect(viewportPixelArea.x, viewportPixelArea.y + firstH, texW, secondH)));

                GUI.DrawTextureWithTexCoords(
                    secondRect,
                    creditsTexture,
                    new Rect(0.0f, 1.0f - secondH / (float)texH, 1.0f, secondH / (float)texH));
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if (creditsTexture != null) Object.Destroy(creditsTexture);
    }

    // options_done key is opt_done bitmap
    public AboutMenu() : base(MenuID.About)
    {
        AssignBackground("about_bk");

        // add done button
        var doneButton = AddBMButton("opt_done", "options_done", 0.05f, 0.9f, 4);
        doneButton.Sound = MenuSound.BeepDouble;
        doneButton.OnClick += () =>
        {
            if (MenuManager.Instance != null) MenuManager.Instance.GoBack();
        };

        // load credits image
        using(var stream = AssetManager.Open("jpg", "credits.jpg"))
        {
            if (stream != null) creditsTexture = ImageLoader.LoadTexture(stream, ImageLoader.TextureFormat.JPG);
        }

        // load draw position
        WidgetTuning.RetrieveWidgetData((int)this.ID, "Credits", out creditsDrawArea);
    }
}
