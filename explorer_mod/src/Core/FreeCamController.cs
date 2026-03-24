using Godot;
using System;

namespace GodotExplorer.Core;

/// <summary>
/// Manages a free-roaming Camera2D that can be detached from the game's camera.
/// WASD to pan, mouse wheel to zoom, middle-click drag to pan.
/// </summary>
public class FreeCamController
{
    private readonly SceneTree _sceneTree;
    private Camera2D? _freeCam;
    private ulong _originalCameraId;
    private Vector2 _moveDir;
    private bool _middleMouseDragging;

    public bool IsActive => _freeCam != null && GodotObject.IsInstanceValid(_freeCam);
    public float MoveSpeed { get; set; } = 400f;
    public float ZoomStep { get; set; } = 1.1f;
    public Vector2 Position => _freeCam?.Position ?? Vector2.Zero;
    public Vector2 Zoom => _freeCam?.Zoom ?? Vector2.One;

    public event Action<bool>? ActiveChanged;

    /// <summary>
    /// Set the movement direction (called from input polling).
    /// </summary>
    public void SetMoveDirection(Vector2 dir)
    {
        _moveDir = dir;
    }

    public FreeCamController(SceneTree sceneTree)
    {
        _sceneTree = sceneTree;
    }

    public void Enable()
    {
        if (IsActive) return;

        // Find the current active Camera2D
        Camera2D? original = FindCurrentCamera();
        if (original != null)
        {
            _originalCameraId = original.GetInstanceId();
        }

        // Create our freecam
        _freeCam = new Camera2D();
        _freeCam.Name = "GodotExplorer_FreeCam";

        if (original != null)
        {
            _freeCam.Position = original.GlobalPosition;
            _freeCam.Zoom = original.Zoom;
        }

        _sceneTree.Root.AddChild(_freeCam);
        _freeCam.MakeCurrent();

        GD.Print("[GodotExplorer] Freecam enabled.");
        ActiveChanged?.Invoke(true);
    }

    public void Disable()
    {
        if (!IsActive) return;

        // Restore original camera
        var originalObj = GodotObject.InstanceFromId(_originalCameraId);
        if (originalObj is Camera2D originalCam && GodotObject.IsInstanceValid(originalCam))
        {
            originalCam.MakeCurrent();
        }

        _freeCam!.QueueFree();
        _freeCam = null;
        _originalCameraId = 0;
        _moveDir = Vector2.Zero;
        _middleMouseDragging = false;

        GD.Print("[GodotExplorer] Freecam disabled.");
        ActiveChanged?.Invoke(false);
    }

    public void Toggle()
    {
        if (IsActive) Disable();
        else Enable();
    }

    /// <summary>
    /// Process an input event. Returns true if consumed.
    /// </summary>
    public bool ProcessInput(InputEvent @event)
    {
        if (!IsActive) return false;

        if (@event is InputEventKey keyEvent)
        {
            return HandleKeyInput(keyEvent);
        }

        if (@event is InputEventMouseButton mouseBtn)
        {
            return HandleMouseButton(mouseBtn);
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            return HandleMouseMotion(mouseMotion);
        }

        return false;
    }

    /// <summary>
    /// Called every frame to apply movement. Should be called from a process callback.
    /// </summary>
    public void Process(double delta)
    {
        if (!IsActive || _freeCam == null) return;

        // Validate original camera still exists, auto-disable if not
        if (_originalCameraId != 0)
        {
            var obj = GodotObject.InstanceFromId(_originalCameraId);
            if (obj == null || !GodotObject.IsInstanceValid(obj as GodotObject))
            {
                _originalCameraId = 0;
            }
        }

        if (_moveDir != Vector2.Zero)
        {
            // Scale speed inversely with zoom (zoomed out = faster pan)
            float zoomScale = 1.0f / _freeCam.Zoom.X;
            _freeCam.Position += _moveDir * MoveSpeed * zoomScale * (float)delta;
        }
    }

    private bool HandleKeyInput(InputEventKey keyEvent)
    {
        // Build movement direction from WASD
        var key = keyEvent.Keycode;
        bool pressed = keyEvent.Pressed;

        switch (key)
        {
            case Key.W: case Key.Up:
                _moveDir.Y = pressed ? -1 : (_moveDir.Y < 0 ? 0 : _moveDir.Y);
                return true;
            case Key.S: case Key.Down:
                _moveDir.Y = pressed ? 1 : (_moveDir.Y > 0 ? 0 : _moveDir.Y);
                return true;
            case Key.A: case Key.Left:
                _moveDir.X = pressed ? -1 : (_moveDir.X < 0 ? 0 : _moveDir.X);
                return true;
            case Key.D: case Key.Right:
                _moveDir.X = pressed ? 1 : (_moveDir.X > 0 ? 0 : _moveDir.X);
                return true;
            case Key.Shift:
                MoveSpeed = pressed ? 800f : 400f;
                return true;
        }

        return false;
    }

    private bool HandleMouseButton(InputEventMouseButton mouseBtn)
    {
        if (mouseBtn.ButtonIndex == MouseButton.WheelUp && mouseBtn.Pressed)
        {
            if (_freeCam != null)
                _freeCam.Zoom *= ZoomStep;
            return true;
        }

        if (mouseBtn.ButtonIndex == MouseButton.WheelDown && mouseBtn.Pressed)
        {
            if (_freeCam != null)
                _freeCam.Zoom /= ZoomStep;
            return true;
        }

        if (mouseBtn.ButtonIndex == MouseButton.Middle)
        {
            _middleMouseDragging = mouseBtn.Pressed;
            return true;
        }

        return false;
    }

    private bool HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (_middleMouseDragging && _freeCam != null)
        {
            float zoomScale = 1.0f / _freeCam.Zoom.X;
            _freeCam.Position -= mouseMotion.Relative * zoomScale;
            return true;
        }
        return false;
    }

    private Camera2D? FindCurrentCamera()
    {
        // Search for the active Camera2D in the scene
        var cameras = _sceneTree.Root.FindChildren("*", "Camera2D", true, false);
        foreach (var node in cameras)
        {
            if (node is Camera2D cam && cam.IsCurrent())
                return cam;
        }
        return null;
    }
}
