using DualSenseCompanion.App;
using DualSenseCompanion.Device;

namespace DualSenseCompanion.Features;

//Touchpad as a laptop-style trackpad: one finger moves, click = left click,
//click with two fingers = right click, two-finger drag = scroll
public sealed class TouchpadMouse
{
    private const float ScrollUnitsPerWheelNotch = 60f;

    private readonly SubPixelAccumulator _move = new SubPixelAccumulator();
    private int? _lastId;
    private int _lastX, _lastY;
    private float? _lastScrollY;
    private float _scrollRemainder;
    private bool _leftDown;
    private bool _rightDown;

    public void Update(DualSenseState s, TouchpadSettings t)
    {
        if (!s.HasExtendedData)
        {
            return;
        }

        bool twoFingers = s.Touch1.Active && s.Touch2.Active;
        bool clicked = s.IsPressed(DsButtons.Touchpad);

        UpdateButtons(clicked, twoFingers);

        if (twoFingers && t.TwoFingerScroll && !clicked)
        {
            _lastId = null;
            float y = (s.Touch1.Y + s.Touch2.Y) / 2f;

            if (_lastScrollY.HasValue)
            {
                _scrollRemainder += (_lastScrollY.Value - y) * t.Sensitivity;
                int notches = (int)(_scrollRemainder / ScrollUnitsPerWheelNotch);

                if (notches != 0)
                {
                    _scrollRemainder -= notches * ScrollUnitsPerWheelNotch;

                    //Drag up scrolls the page up
                    MouseInput.Wheel(-notches * MouseInput.WheelDelta);
                }
            }

            _lastScrollY = y;
            return;
        }

        _lastScrollY = null;
        _scrollRemainder = 0;

        if (!s.Touch1.Active)
        {
            _lastId = null;
            return;
        }

        if (_lastId == s.Touch1.Id)
        {
            var (dx, dy) = _move.Add((s.Touch1.X - _lastX) * t.Sensitivity, (s.Touch1.Y - _lastY) * t.Sensitivity);
            MouseInput.Move(dx, dy);
        }
        else
        {
            _move.Reset();
        }

        _lastId = s.Touch1.Id;
        _lastX = s.Touch1.X;
        _lastY = s.Touch1.Y;
    }

    private void UpdateButtons(bool clicked, bool twoFingers)
    {
        if (clicked && !_leftDown && !_rightDown)
        {
            if (twoFingers)
            {
                MouseInput.RightButton(true);
                _rightDown = true;
            }
            else
            {
                MouseInput.LeftButton(true);
                _leftDown = true;
            }
        }
        else if (!clicked)
        {
            ReleaseButtons();
        }
    }

    public void ReleaseButtons()
    {
        if (_leftDown)
        {
            MouseInput.LeftButton(false);
            _leftDown = false;
        }

        if (_rightDown)
        {
            MouseInput.RightButton(false);
            _rightDown = false;
        }
    }
}
