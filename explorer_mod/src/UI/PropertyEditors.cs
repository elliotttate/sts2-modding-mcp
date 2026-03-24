using Godot;
using System;

namespace GodotExplorer.UI;

/// <summary>
/// Factory for creating type-appropriate editor widgets for Godot properties.
/// Each editor reads a current value and calls onChange when the user modifies it.
/// </summary>
public static class PropertyEditors
{
    /// <summary>
    /// Create an editor Control for the given property type/hint.
    /// </summary>
    public static Control Create(
        Variant.Type type,
        PropertyHint hint,
        string hintString,
        Variant currentValue,
        bool readOnly,
        Action<Variant> onChange)
    {
        return type switch
        {
            Variant.Type.Bool => CreateBoolEditor(currentValue.AsBool(), readOnly, onChange),
            Variant.Type.Int => CreateIntEditor(currentValue, hint, hintString, readOnly, onChange),
            Variant.Type.Float => CreateFloatEditor(currentValue, hint, hintString, readOnly, onChange),
            Variant.Type.String => CreateStringEditor(currentValue.AsString(), readOnly, onChange),
            Variant.Type.Vector2 => CreateVector2Editor(currentValue.AsVector2(), readOnly, onChange),
            Variant.Type.Vector2I => CreateVector2IEditor(currentValue.AsVector2I(), readOnly, onChange),
            Variant.Type.Color => CreateColorEditor(currentValue.AsColor(), readOnly, onChange),
            Variant.Type.NodePath => CreateNodePathEditor(currentValue.AsNodePath().ToString(), readOnly),
            _ when hint == PropertyHint.Enum => CreateEnumEditor(currentValue.AsInt32(), hintString, readOnly, onChange),
            _ => CreateReadOnlyLabel(currentValue, type)
        };
    }

    private static Control CreateBoolEditor(bool value, bool readOnly, Action<Variant> onChange)
    {
        var cb = new CheckBox();
        cb.ButtonPressed = value;
        cb.Disabled = readOnly;
        cb.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        if (!readOnly)
            cb.Toggled += (pressed) => onChange(pressed);
        return cb;
    }

    private static Control CreateIntEditor(Variant value, PropertyHint hint, string hintString, bool readOnly, Action<Variant> onChange)
    {
        if (hint == PropertyHint.Enum)
            return CreateEnumEditor(value.AsInt32(), hintString, readOnly, onChange);

        var spinBox = new SpinBox();
        spinBox.Step = 1;
        spinBox.Rounded = true;
        spinBox.AllowGreater = true;
        spinBox.AllowLesser = true;
        spinBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        spinBox.Editable = !readOnly;

        if (hint == PropertyHint.Range && !string.IsNullOrEmpty(hintString))
            ApplyRange(spinBox, hintString);

        spinBox.Value = value.AsDouble();

        if (!readOnly)
            spinBox.ValueChanged += (val) => onChange((int)val);

        StyleSpinBox(spinBox);
        return spinBox;
    }

    private static Control CreateFloatEditor(Variant value, PropertyHint hint, string hintString, bool readOnly, Action<Variant> onChange)
    {
        var spinBox = new SpinBox();
        spinBox.Step = 0.01;
        spinBox.AllowGreater = true;
        spinBox.AllowLesser = true;
        spinBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        spinBox.Editable = !readOnly;

        if (hint == PropertyHint.Range && !string.IsNullOrEmpty(hintString))
            ApplyRange(spinBox, hintString);

        spinBox.Value = value.AsDouble();

        if (!readOnly)
            spinBox.ValueChanged += (val) => onChange((float)val);

        StyleSpinBox(spinBox);
        return spinBox;
    }

    private static Control CreateStringEditor(string value, bool readOnly, Action<Variant> onChange)
    {
        var lineEdit = new LineEdit();
        lineEdit.Text = value ?? "";
        lineEdit.Editable = !readOnly;
        lineEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ExplorerTheme.StyleLineEdit(lineEdit);

        if (!readOnly)
            lineEdit.TextSubmitted += (text) => onChange(text);

        return lineEdit;
    }

    private static Control CreateVector2Editor(Vector2 value, bool readOnly, Action<Variant> onChange)
    {
        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 4);

        var xLabel = new Label { Text = "X:" };
        ExplorerTheme.StyleLabel(xLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        hbox.AddChild(xLabel);

        var xSpin = new SpinBox();
        xSpin.Step = 0.1;
        xSpin.AllowGreater = true;
        xSpin.AllowLesser = true;
        xSpin.Value = value.X;
        xSpin.Editable = !readOnly;
        xSpin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        StyleSpinBox(xSpin);
        hbox.AddChild(xSpin);

        var yLabel = new Label { Text = "Y:" };
        ExplorerTheme.StyleLabel(yLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        hbox.AddChild(yLabel);

        var ySpin = new SpinBox();
        ySpin.Step = 0.1;
        ySpin.AllowGreater = true;
        ySpin.AllowLesser = true;
        ySpin.Value = value.Y;
        ySpin.Editable = !readOnly;
        ySpin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        StyleSpinBox(ySpin);
        hbox.AddChild(ySpin);

        if (!readOnly)
        {
            void Notify(double _) => onChange(new Vector2((float)xSpin.Value, (float)ySpin.Value));
            xSpin.ValueChanged += Notify;
            ySpin.ValueChanged += Notify;
        }

        return hbox;
    }

    private static Control CreateVector2IEditor(Vector2I value, bool readOnly, Action<Variant> onChange)
    {
        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 4);

        var xLabel = new Label { Text = "X:" };
        ExplorerTheme.StyleLabel(xLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        hbox.AddChild(xLabel);

        var xSpin = new SpinBox();
        xSpin.Step = 1;
        xSpin.Rounded = true;
        xSpin.AllowGreater = true;
        xSpin.AllowLesser = true;
        xSpin.Value = value.X;
        xSpin.Editable = !readOnly;
        xSpin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        StyleSpinBox(xSpin);
        hbox.AddChild(xSpin);

        var yLabel = new Label { Text = "Y:" };
        ExplorerTheme.StyleLabel(yLabel, ExplorerTheme.TextDim, ExplorerTheme.FontSizeSmall);
        hbox.AddChild(yLabel);

        var ySpin = new SpinBox();
        ySpin.Step = 1;
        ySpin.Rounded = true;
        ySpin.AllowGreater = true;
        ySpin.AllowLesser = true;
        ySpin.Value = value.Y;
        ySpin.Editable = !readOnly;
        ySpin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        StyleSpinBox(ySpin);
        hbox.AddChild(ySpin);

        if (!readOnly)
        {
            void Notify(double _) => onChange(new Vector2I((int)xSpin.Value, (int)ySpin.Value));
            xSpin.ValueChanged += Notify;
            ySpin.ValueChanged += Notify;
        }

        return hbox;
    }

    private static Control CreateColorEditor(Color value, bool readOnly, Action<Variant> onChange)
    {
        var picker = new ColorPickerButton();
        picker.Color = value;
        picker.EditAlpha = true;
        picker.CustomMinimumSize = new Vector2(60, 28);
        picker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        picker.Disabled = readOnly;

        if (!readOnly)
            picker.ColorChanged += (color) => onChange(color);

        return picker;
    }

    private static Control CreateEnumEditor(int value, string hintString, bool readOnly, Action<Variant> onChange)
    {
        var optionBtn = new OptionButton();
        optionBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        optionBtn.Disabled = readOnly;

        // Parse hint_string: "Option1,Option2,Option3" or "Option1:0,Option2:1,..."
        string[] options = hintString.Split(',');
        for (int i = 0; i < options.Length; i++)
        {
            string opt = options[i].Trim();
            if (opt.Contains(':'))
            {
                var parts = opt.Split(':');
                optionBtn.AddItem(parts[0].Trim(), int.Parse(parts[1].Trim()));
            }
            else
            {
                optionBtn.AddItem(opt, i);
            }
        }

        // Select current value
        for (int i = 0; i < optionBtn.ItemCount; i++)
        {
            if (optionBtn.GetItemId(i) == value)
            {
                optionBtn.Selected = i;
                break;
            }
        }

        if (!readOnly)
            optionBtn.ItemSelected += (idx) => onChange(optionBtn.GetItemId((int)idx));

        return optionBtn;
    }

    private static Control CreateNodePathEditor(string value, bool readOnly)
    {
        var lineEdit = new LineEdit();
        lineEdit.Text = value;
        lineEdit.Editable = false; // NodePaths are always read-only in our explorer
        lineEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ExplorerTheme.StyleLineEdit(lineEdit);
        return lineEdit;
    }

    private static Control CreateReadOnlyLabel(Variant value, Variant.Type type)
    {
        var label = new Label();
        string text;
        try
        {
            text = value.Obj?.ToString() ?? "(null)";
            if (text.Length > 80)
                text = text[..77] + "...";
        }
        catch
        {
            text = $"<{Core.PropertyHelper.TypeName(type)}>";
        }
        label.Text = text;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ExplorerTheme.StyleLabel(label, ExplorerTheme.TextDim);
        return label;
    }

    private static void ApplyRange(SpinBox spinBox, string hintString)
    {
        // Format: "min,max" or "min,max,step" or "min,max,step,or_greater,or_lesser"
        var parts = hintString.Split(',');
        if (parts.Length >= 2)
        {
            if (double.TryParse(parts[0].Trim(), out double min)) spinBox.MinValue = min;
            if (double.TryParse(parts[1].Trim(), out double max)) spinBox.MaxValue = max;
        }
        if (parts.Length >= 3)
        {
            if (double.TryParse(parts[2].Trim(), out double step)) spinBox.Step = step;
        }
        for (int i = 3; i < parts.Length; i++)
        {
            string flag = parts[i].Trim().ToLowerInvariant();
            if (flag == "or_greater") spinBox.AllowGreater = true;
            if (flag == "or_lesser" || flag == "or_less") spinBox.AllowLesser = true;
        }
    }

    private static void StyleSpinBox(SpinBox spinBox)
    {
        // SpinBox contains an internal LineEdit we can style
        var lineEdit = spinBox.GetLineEdit();
        if (lineEdit != null)
        {
            ExplorerTheme.StyleLineEdit(lineEdit);
        }
    }
}
