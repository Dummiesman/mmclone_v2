using System;
using UnityEngine;

public class BMButton : UIWidget
{
    public Action OnClick;
    public MenuSound Sound = MenuSound.None;

    public BMButtonGroup Group = null;
    public int BaseState = 0;

    public Vector2 HitRegionSize
    {
        get
        {
            if (texture == null)
            {
                return Vector2.zero;
            }
            else
            {
                float fHeight = texture.Texture.height;
                float fWidth = texture.Texture.width;

                return new Vector2(fWidth, fHeight / numStates);
            }
        }
    }

    private AGETexture texture;
    private int numStates;

    private bool focused;
    private bool pressed;

    //
    public void SetTexture(string name)
    {
        if (texture != null && texture.Name == name) return;
        if (texture != null)
        {
            texture.Destroy();
        }
        texture = TextureLoader.Load(name);
        if (texture != null)
        {
            texture.Texture.wrapMode = TextureWrapMode.Clamp;
        }
    }

    // Events
    public override void Focus()
    {
        base.Focus();
        focused = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        focused = false;
    }

    public override void Draw()
    {
        base.Draw();
        if (texture != null)
        {
            int stateIndex = BaseState;
            if(!Enabled)
            {
                stateIndex = numStates - 1; // disabled state
            }
            else if(numStates == 4 || numStates == 3)
            {
                if (pressed)
                {
                    stateIndex = 2;
                }
                else if (focused)
                {
                    stateIndex = 1;
                }
            }
            else if(numStates == 5 || numStates == 6)
            {
                if(pressed)
                {
                    stateIndex = 1;
                }
                else if (focused)
                {
                    stateIndex = 3;
                }
                else if (Group != null && Group.ActiveButton == this)
                {
                    stateIndex = 2; // active state
                }
            }

            if (stateIndex < numStates)
            {
                float texH = texture.Texture.height;
                float ySizePerState = 1.0f / numStates;
                float baseY = 1.0f - (ySizePerState * (stateIndex + 1));
                
                // to prevent filtering artifacts, offset tex coords slightly
                float halfTexel = 0.5f / texH;
                Rect texCoords = new Rect(0.0f, baseY + halfTexel,
                                          1.0f, ySizePerState - (2.0f * halfTexel));

                var ourArea = Menu.ToPixelCoordinates(this.Rect);
                GUI.DrawTextureWithTexCoords(ourArea, texture, texCoords);
            }
        }
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input)) return true;
        if (input == UIEventType.MouseDown && focused && MouseContained())
        {
            pressed = true;
            if(Sound != MenuSound.None  && MenuManager.Instance != null)
            {
                MenuManager.Instance.PlaySound(Sound);
            }
            if(Group != null)
            {
                Group.ActiveButton = this;
            }
            Menu.FocusLock(this);
            return true;
        }
        if (input == UIEventType.MouseUp && pressed)
        {
            pressed = false;
            Menu.ReleaseFocusLock(this);

            if(MouseContained())
            {
                // fire onclick if mouse was released within the button bounds
                OnClick?.Invoke();
            }
            return true;
        }
        if(input == UIEventType.Enter && focused)
        {
            if (Sound != MenuSound.None && MenuManager.Instance != null)
            {
                MenuManager.Instance.PlaySound(Sound);
            }
            if (Group != null)
            {
                Group.ActiveButton = this;
            }
            Menu.ReleaseFocusLock(this);
            OnClick?.Invoke();
            return true;
        }
        return base.HandleInput(input);
    }

    public override void Dispose()
    {
        if (texture != null) UnityEngine.Object.Destroy(texture);
    }

    public BMButton(UIMenu menu, string name, int id, int numStates, Rect rect) : base(menu, id, name, rect)
    {
        this.numStates = numStates;
        SetTexture(name);
    }
}
