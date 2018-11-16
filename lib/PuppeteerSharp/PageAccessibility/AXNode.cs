﻿using System;
using System.Linq;
using System.Collections.Generic;
using PuppeteerSharp.Messaging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp.Helpers;

namespace PuppeteerSharp.PageAccessibility
{
    internal class AXNode
    {
        internal AccessibilityGetFullAXTreeResponse Payload { get; }
        public List<AXNode> Children { get; }

        private readonly string _name;
        private string _role;
        private bool _richlyEditable;
        private bool _editable;
        private bool _focusable;
        private bool _expanded;
        private bool? _cachedHasFocusableChild;

        public AXNode(AccessibilityGetFullAXTreeResponse payload)
        {
            Payload = payload;
            Children = new List<AXNode>();

            _name = payload.Name != null ? payload.Name.Value.ToObject<string>() : string.Empty;
            _role = payload.Role ?? "Unknown";

            foreach (var property in payload.Properties)
            {
                if (property.Name == "editable")
                {
                    _richlyEditable = property.Value.Value.ToObject<string>() == "richtext";
                    _editable = true;
                }
                if (property.Name == "focusable")
                {
                    _focusable = property.Value.Value.ToObject<bool>();
                }
                if (property.Name == "expanded")
                {
                    _expanded = property.Value.Value.ToObject<bool>();
                }
            }
        }

        internal static AXNode CreateTree(AccessibilityGetFullAXTreeResponse[] payloads)
        {
            var nodeById = new Dictionary<string, AXNode>();
            foreach (var payload in payloads)
            {
                nodeById[payload.NodeId] = new AXNode(payload);
            }
            foreach (var node in nodeById.Values)
            {
                foreach (var childId in node.Payload.ChildIds)
                {
                    node.Children.Add(nodeById[childId]);
                }
            }
            return nodeById.Values.FirstOrDefault();
        }

        private bool IsPlainTextField()
        {
            if (_richlyEditable)
            {
                return false;
            }
            if (_editable)
            {
                return true;
            }
            return _role == "textbox" || _role == "ComboBox" || _role == "searchbox";
        }

        private bool IsTextOnlyObject()
            => _role == "LineBreak" ||
                _role == "text" ||
                _role == "InlineTextBox";

        private bool HasFocusableChild()
        {
            if (!_cachedHasFocusableChild.HasValue)
            {
                _cachedHasFocusableChild = false;
                foreach (var child in Children)
                {
                    if (child._focusable || child.HasFocusableChild())
                    {
                        _cachedHasFocusableChild = true;
                        break;
                    }
                }
            }
            return _cachedHasFocusableChild.Value;
        }

        internal bool IsLeafNode()
        {
            if (Children.Count == 0)
            {
                return true;
            }

            // These types of objects may have children that we use as internal
            // implementation details, but we want to expose them as leaves to platform
            // accessibility APIs because screen readers might be confused if they find
            // any children.
            if (IsPlainTextField() || IsTextOnlyObject())
            {
                return true;
            }

            // Roles whose children are only presentational according to the ARIA and
            // HTML5 Specs should be hidden from screen readers.
            // (Note that whilst ARIA buttons can have only presentational children, HTML5
            // buttons are allowed to have content.)
            switch (_role)
            {
                case "doc-cover":
                case "graphics-symbol":
                case "img":
                case "Meter":
                case "scrollbar":
                case "slider":
                case "separator":
                case "progressbar":
                    return true;
            }

            // Here and below: Android heuristics
            if (HasFocusableChild())
            {
                return false;
            }
            if (_focusable && !string.IsNullOrEmpty(_name))
            {
                return true;
            }
            if (_role == "heading" && !string.IsNullOrEmpty(_name))
            {
                return true;
            }
            return false;
        }

        internal bool IsControl()
        {
            switch (_role)
            {
                case "button":
                case "checkbox":
                case "ColorWell":
                case "combobox":
                case "DisclosureTriangle":
                case "listbox":
                case "menu":
                case "menubar":
                case "menuitem":
                case "menuitemcheckbox":
                case "menuitemradio":
                case "radio":
                case "scrollbar":
                case "searchbox":
                case "slider":
                case "spinbutton":
                case "switch":
                case "tab":
                case "textbox":
                case "tree":
                    return true;
                default:
                    return false;
            }
        }

        internal bool IsInteresting(bool insideControl)
        {
            if (_role == "Ignored")
            {
                return false;
            }

            if (_focusable || _richlyEditable)
            {
                return true;
            }
            // If it's not focusable but has a control role, then it's interesting.
            if (IsControl())
            {
                return true;
            }
            // A non focusable child of a control is not interesting
            if (insideControl)
            {
                return false;
            }
            return IsLeafNode() && !string.IsNullOrEmpty(_name);
        }

        internal SerializedAXNode Serialize()
        {
            var properties = new Dictionary<string, JToken>();
            foreach (var property in Payload.Properties)
            {
                properties[property.Name.ToLower()] = property.Value.Value;
            }

            if (Payload.Name != null)
            {
                properties["name"] = Payload.Name.Value;
            }
            if (Payload.Value != null)
            {
                properties["value"] = Payload.Value.Value;
            }
            if (Payload.Description != null)
            {
                properties["description"] = Payload.Description.Value;
            }

            var node = new SerializedAXNode
            {
                Role = _role,
                Name = properties.GetValueOrDefault("name")?.ToObject<string>(),
                Value = properties.GetValueOrDefault("value")?.ToObject<string>(),
                Description = properties.GetValueOrDefault("description")?.ToObject<string>(),
                KeyShortcuts = properties.GetValueOrDefault("keyshortcuts")?.ToObject<string>(),
                RoleDescription = properties.GetValueOrDefault("roledescription")?.ToObject<string>(),
                ValueText = properties.GetValueOrDefault("valuetext")?.ToObject<string>(),
                Disabled = properties.GetValueOrDefault("disabled")?.ToObject<bool>() ?? false,
                Expanded = properties.GetValueOrDefault("expanded")?.ToObject<bool>() ?? false,
                // WebArea"s treat focus differently than other nodes. They report whether their frame  has focus,
                // not whether focus is specifically on the root node.
                Focused = properties.GetValueOrDefault("focused")?.ToObject<bool>() == true && _role != "WebArea",
                Modal = properties.GetValueOrDefault("modal")?.ToObject<bool>() ?? false,
                Multiline = properties.GetValueOrDefault("multiline")?.ToObject<bool>() ?? false,
                Multiselectable = properties.GetValueOrDefault("multiselectable")?.ToObject<bool>() ?? false,
                Readonly = properties.GetValueOrDefault("readonly")?.ToObject<bool>() ?? false,
                Required = properties.GetValueOrDefault("required")?.ToObject<bool>() ?? false,
                Selected = properties.GetValueOrDefault("selected")?.ToObject<bool>() ?? false,
                Checked = GetCheckedState(properties.GetValueOrDefault("checked")?.ToObject<string>()),
                Pressed = GetCheckedState(properties.GetValueOrDefault("pressed")?.ToObject<string>()),
                Level = properties.GetValueOrDefault("level")?.ToObject<int>() ?? 0,
                ValueMax = properties.GetValueOrDefault("valuemax")?.ToObject<int>() ?? 0,
                ValueMin = properties.GetValueOrDefault("valuemin")?.ToObject<int>() ?? 0,
                AutoComplete = GetIfNotFalse(properties.GetValueOrDefault("autocomplete")?.ToObject<string>()),
                HasPopup = GetIfNotFalse(properties.GetValueOrDefault("haspopup")?.ToObject<string>()),
                Invalid = GetIfNotFalse(properties.GetValueOrDefault("invalid")?.ToObject<string>()),
                Orientation = GetIfNotFalse(properties.GetValueOrDefault("orientation")?.ToObject<string>())
            };

            return node;
        }

        private string GetIfNotFalse(string value) => value != null && value != "false" ? value : null;

        private CheckedState GetCheckedState(string value)
        {
            switch (value)
            {
                case "mixed":
                    return CheckedState.Mixed;
                case "true":
                    return CheckedState.True;
                default:
                    return CheckedState.False;
            }
        }
    }
}