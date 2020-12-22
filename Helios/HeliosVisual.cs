﻿//  Copyright 2014 Craig Courtney
//  Copyright 2020 Helios Contributors
//    
//  Helios is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  Helios is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using GadrocsWorkshop.Helios.ComponentModel;

namespace GadrocsWorkshop.Helios
{
    /// <summary>
    /// HeliosVisual objects are helios objects which will be rendered on screen.
    /// </summary>
    public abstract class HeliosVisual : HeliosObject
    {
        private HeliosVisualRenderer _renderer;

        private WeakReference _parent = new WeakReference(null);
        private string _typeIdentifier;
        private Rect _rectangle;
        private Rect _adjustedRectangle;
        private HeliosVisualRotation _rotation = HeliosVisualRotation.None;

        private bool _locked;
        private bool _snapTarget = true;
        private bool _hidden;
        private bool _defaultHidden;

        private readonly HeliosValue _hiddenValue;

        /// <summary>
        /// Base constructor for a HeliosVisual.
        /// </summary>
        /// <param name="name">Default name for this object.</param>
        /// <param name="nativeSize">Native size that this control renderes at.</param>
        protected HeliosVisual(string name, Size nativeSize)
            : base(name)
        {
            _rectangle = new Rect(nativeSize);
            NativeSize = nativeSize;
            UpdateRectangle();

            Children = new HeliosVisualCollection();

            HeliosAction toggleVisibleAction = new HeliosAction(this, "", "hidden", "toggle", "Toggles whether this control is displayed on screen.");
            toggleVisibleAction.Execute += ToggleVisibleAction_Execute;
            Actions.Add(toggleVisibleAction);

            _hiddenValue = new HeliosValue(this, new BindingValue(false), "", "hidden", "Indicates if this control is hidden and off screen.", "True if this panel is not displayed.", BindingValueUnits.Boolean);
            _hiddenValue.Execute += SetHiddenAction_Execute;
            Values.Add(_hiddenValue);
            Actions.Add(_hiddenValue);

            Children.CollectionChanged += Children_CollectionChanged;
        }

        /// <summary>
        /// Event triggered when this panel should be re-displayed.
        /// </summary>
        public event EventHandler DisplayUpdate;

        public event EventHandler Resized;

        public event EventHandler Moved;

        public event EventHandler HiddenChanged;

        #region Properties

        public NonClickableZone[] NonClickableZones { get; set; }

        public bool PersistChildren { get; set; } = true;

        public override string TypeIdentifier
        {
            get
            {
                if (_typeIdentifier != null)
                {
                    return _typeIdentifier;
                }

                HeliosDescriptor descriptor = ConfigManager.ModuleManager.ControlDescriptors[GetType()];
                _typeIdentifier = descriptor.TypeIdentifier;
                return _typeIdentifier;
            }
        }

        /// <summary>
        /// XXX this needs a rewrite, as it violates what a property is supposed to be.  Setting it and then reading it won't necessarily return the same value
        /// 
        /// on get, recursively checks ancestry to see if any visuals are in design mode or the profile is in design mode
        /// on set, just sets this object to design mode
        /// </summary>
        public override bool DesignMode
        {
            get => base.DesignMode || (Profile != null && Profile.DesignMode) || (Parent != null && Parent.DesignMode);
            set => base.DesignMode = value;
        }

        public Monitor Monitor
        {
            get
            {
                HeliosVisual visual = this;
                while (visual.Parent != null)
                {
                    visual = visual.Parent;
                }
                return visual as Monitor;
            }
        }

        /// <summary>
        /// Gets the this visuals parent visual
        /// </summary>
        public HeliosVisual Parent
        {
            get => _parent.Target as HeliosVisual;
            set => _parent = new WeakReference(value);
        }

        /// <summary>
        /// Returns the collection of child visuals for this visual.
        /// </summary>
        public HeliosVisualCollection Children { get; }

        /// <summary>
        /// Returns true if this visual is hidden and not displayed on the screen.
        /// </summary>
        public bool IsHidden
        {
            get => _hidden;
            set
            {
                if (_hidden.Equals(value))
                {
                    return;
                }

                _hidden = value;
                _hiddenValue.SetValue(new BindingValue(_hidden), false);
                OnPropertyChanged("IsHidden", !value, value, false);
                OnHiddenChanged();
            }
        }

        /// <summary>
        /// Gets or sets whether this control is hidden load/reset of a profile.
        /// </summary>
        public bool IsDefaultHidden
        {
            get => _defaultHidden;
            set
            {
                if (_defaultHidden.Equals(value))
                {
                    return;
                }

                bool oldValue = _defaultHidden;
                _defaultHidden = value;
                OnPropertyChanged("IsDefaultHidden", oldValue, value, true);
            }
        }

        /// <summary>
        /// Recursively checks visibility of all visuals up to the root; only returns true if none of them are hidden
        /// </summary>
        public bool IsVisible => (!IsHidden) && (Parent == null || Parent.IsVisible);

        /// <summary>
        /// Gets and sets the lock status for this visual.  When locked it cannot be selected, moved or edited.
        /// </summary>
        public bool IsLocked
        {
            get => _locked;
            set
            {
                if (_locked.Equals(value))
                {
                    return;
                }

                bool oldValue = _locked;
                _locked = value;
                OnPropertyChanged("IsLocked", oldValue, value, false);
            }
        }

        /// <summary>
        /// Gets and sets the snap status for this visual.  When true other visuals will snap to this visual when they are moved or resized.
        /// </summary>
        public bool IsSnapTarget
        {
            get => _snapTarget;
            set
            {
                if (_snapTarget.Equals(value))
                {
                    return;
                }

                bool oldValue = _snapTarget;
                _snapTarget = value;
                OnPropertyChanged("IsSnapTarget", oldValue, value, true);
            }
        }

        /// <summary>
        /// Lazy creates and returns the renderer for this visual
        /// </summary>
        public virtual HeliosVisualRenderer Renderer => _renderer ?? (_renderer = ConfigManager.ModuleManager.CreaterRenderer(this));

        /// <summary>
        /// Top edge of where this visual will be displayed.
        /// </summary>
        public double Top
        {
            get => _rectangle.Top;
            set
            {
                double newValue = Math.Truncate(value);
                if (_rectangle.Top.Equals(newValue))
                {
                    return;
                }

                double oldValue = _rectangle.Top;
                _rectangle.Y = newValue;
                OnPropertyChanged("Top", oldValue, newValue, true);
                UpdateRectangle();
                OnMoved();
            }
        }

        /// <summary>
        /// Left edge of where this visual will be displayed
        /// </summary>
        public double Left
        {
            get => _rectangle.Left;
            set
            {
                double newValue = Math.Truncate(value);
                if (_rectangle.Left.Equals(newValue))
                {
                    return;
                }

                double oldValue = _rectangle.Left;
                _rectangle.X = newValue;
                OnPropertyChanged("Left", oldValue, newValue, true);
                UpdateRectangle();
                OnMoved();
            }
        }

        /// <summary>
        /// Width of this visual
        /// </summary>
        public double Width
        {
            get => _rectangle.Width;
            set
            {
                double newValue = Math.Truncate(value);
                if (_rectangle.Width.Equals(newValue))
                {
                    return;
                }

                double oldValue = _rectangle.Width;
                _rectangle.Width = newValue;
                OnPropertyChanged("Width", oldValue, newValue, true);
                UpdateRectangle();
                Refresh();
                OnResized();
            }
        }

        /// <summary>
        /// Height of this visual
        /// </summary>
        public double Height
        {
            get => _rectangle.Height;
            set
            {
                double newValue = Math.Truncate(value);
                if (_rectangle.Height.Equals(newValue))
                {
                    return;
                }

                double oldValue = _rectangle.Height;
                _rectangle.Height = newValue;
                OnPropertyChanged("Height", oldValue, newValue, true);
                UpdateRectangle();
                Refresh();
                OnResized();
            }
        }


        /// <summary>
        /// access to the entire Rect representing Top, Left, Width, and Height
        /// setting this property only fires Moved and Resized once and does not fire the
        /// property changes for Top, Left, Width, and Height
        /// </summary>
        public Rect Rectangle
        {
            get => _rectangle;
            set
            {
                if (_rectangle == value)
                {
                    return;
                }

                Rect oldValue = _rectangle;
                _rectangle = value;
                UpdateRectangle();
                Refresh();
                // ReSharper disable CompareOfFloatsByEqualityOperator we really do care about every change
                if (Left != oldValue.Left || Top != oldValue.Top)
                {
                    OnMoved();
                }
                if (Width != oldValue.Width || Height != oldValue.Height)
                {
                    OnResized();
                }
                // ReSharper enable CompareOfFloatsByEqualityOperator

                // now fire property change for undo support
                OnPropertyChanged("Rectangle", oldValue, value, true);
            }
        }

        /// <summary>
        /// Native size for this visual.
        /// </summary>
        public Size NativeSize { get; }

        /// <summary>
        /// Rectangle of this visual as it's displayed
        /// </summary>
        public Rect DisplayRectangle
        {
            get => _adjustedRectangle;
            private set
            {
                if (_adjustedRectangle.Equals(value))
                {
                    return;
                }

                Rect oldValue = _adjustedRectangle;
                _adjustedRectangle = value;
                OnPropertyChanged("DisplayRectangle", oldValue, value, false);
            }
        }

        /// <summary>
        /// Gets and sets the rotation of this visual
        /// </summary>
        public HeliosVisualRotation Rotation
        {
            get => _rotation;
            set
            {
                if (_rotation.Equals(value))
                {
                    return;
                }

                HeliosVisualRotation oldValue = _rotation;
                _rotation = value;
                OnPropertyChanged("Rotation", oldValue, value, true);
                UpdateRectangle();
                Refresh();
                OnResized();
            }
        }

        /// <summary>
        /// returns true if this visual view can process more mouse wheel input right now
        /// otherwise the mouse wheel input will be delivered to a containing view
        /// </summary>
        public virtual bool CanConsumeMouseWheel => false;

        #endregion

        #region Actions

        /// <summary>
        /// Set Hidden action on control
        /// </summary>
        /// <param name="action"></param>
        /// <param name="e"></param>
        private void SetHiddenAction_Execute(object action, HeliosActionEventArgs e)
        {
            IsHidden = e.Value.BoolValue;
        }

        /// <summary>
        /// Toggles this visual from being displayed and hidden.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="e"></param>
        private void ToggleVisibleAction_Execute(object action, HeliosActionEventArgs e)
        {
            IsHidden = !IsHidden;
        }

        #endregion

        public override void Reset()
        {
            base.Reset();
            IsHidden = IsDefaultHidden;
        }

        /// <summary>
        /// Updates the display rectangle based on current rotation and co-ordinates
        /// </summary>
        private void UpdateRectangle()
        {
            Matrix m1 = new Matrix();
            Rect newRect = _rectangle;

            switch (Rotation)
            {
                case HeliosVisualRotation.CW:
                    m1.RotateAt(90, _rectangle.X, _rectangle.Y);
                    m1.Translate(_rectangle.Height, 0);
                    newRect.Transform(m1);
                    break;

                case HeliosVisualRotation.CCW:
                    m1.RotateAt(-90, _rectangle.X, _rectangle.Y);
                    m1.Translate(0, _rectangle.Width);
                    newRect.Transform(m1);
                    break;

				case HeliosVisualRotation.ROT180:
					m1.RotateAt(180, _rectangle.X + (_rectangle.Width/2), _rectangle.Y + (_rectangle.Height/2));
					m1.Translate(0, 0);
					newRect.Transform(m1);
					break;

                case HeliosVisualRotation.None:
                    // newRect is already equal to untransformed _rectangle
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            PostUpdateRectangle(DisplayRectangle, newRect);
            DisplayRectangle = newRect;
        }

        /// 
        /// Method that can be implemented by sub classes
        /// 
        protected virtual void PostUpdateRectangle(Rect previous, Rect current) {
            // no code in base
        }


        /// <summary>
        /// Method call used to linear scale this control and it's components.
        /// </summary>
        /// <param name="scaleX"></param>
        /// <param name="scaleY"></param>
        public virtual void ScaleChildren(double scaleX, double scaleY)
        {
            // no code in base
        }

        /// <summary>
        /// Forces renderer to reload any necessary resoruces.
        /// </summary>
        protected void Refresh()
        {
            Renderer?.Refresh();
            OnDisplayUpdate();
        }

        /// <summary>
        /// Fires the display update event.
        /// </summary>
        protected virtual void OnDisplayUpdate()
        {
            DisplayUpdate?.Invoke(this, EventArgs.Empty);
        }

        private void OnMoved()
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }

        private void OnResized()
        {
            Resized?.Invoke(this, EventArgs.Empty);
        }

        private void OnHiddenChanged()
        {
            HiddenChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Method to override default configurations when this is used as an icon.
        /// </summary>
        public virtual void ConfigureIconInstance()
        {
            // no code in base
        }

        /// <summary>
        /// Called to determine if a point inside this location should be handled.  True
        /// should be returned if this control is opaque at that location to prevent
        /// tunneling to visuals below it.
        /// </summary>
        /// <param name="location">Point inside visual boundaries.</param>
        /// <returns>True if this visual should handle the interaction.</returns>
        public virtual bool HitTest(Point location) => true;

        /// <summary>
        /// Called when a mouse wheel is rotated on this control.
        /// </summary>
        /// <param name="delta">Signed change in mouse wheel value.</param>
        public virtual void MouseWheel(int delta)
        {
            // no code in base
        }

        /// <summary>
        /// Called when a mouse button is pressed on this control.
        /// </summary>
        /// <param name="location">Current location of the mouse relative to this controls upper left corner.</param>
        public virtual void MouseDown(Point location)
        {
            // no code in base
        }

        /// <summary>
        /// Called when the mouse is dragged after being pressed on this control.
        /// </summary>
        /// <param name="location">Current location of the mouse relative to this controls upper left corner.</param>
        public virtual void MouseDrag(Point location)
        {
            // no code in base    
        }

        /// <summary>
        /// Called when the mouse button is released after being pressed on this control.
        /// </summary>
        /// <param name="location">Current location of the mouse relative to this controls upper left corner.</param>
        public virtual void MouseUp(Point location)
        {
            // no code in base
        }

        private void Children_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) ||
                (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace))
            {
                foreach (HeliosVisual control in e.NewItems)
                {
                    control.Parent = this;
                    control.Profile = Profile;
                    control.PropertyChanged += Child_PropertyChanged;
                    control.ReconnectBindings();
                }
            }

            if ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) ||
                (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace))
            {
                foreach (HeliosVisual control in e.OldItems)
                {
                    control.Parent = null;
                    control.Profile = Profile;
                    control.PropertyChanged -= Child_PropertyChanged;
                    control.DisconnectBindings();
                }
            }
        }

        public override void DisconnectBindings()
        {
            base.DisconnectBindings();
            foreach (HeliosVisual child in Children)
            {
                child.DisconnectBindings();
            }
        }

        public override void ReconnectBindings()
        {
            base.ReconnectBindings();
            foreach (HeliosVisual child in Children)
            {
                child.ReconnectBindings();
            }
        }

        protected override void OnProfileChanged(HeliosProfile oldProfile)
        {
            base.OnProfileChanged(oldProfile);
            foreach (HeliosVisual child in Children)
            {
                child.Profile = Profile;
            }
        }

        private void Child_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            HeliosVisual control = sender as HeliosVisual;
            OnPropertyChanged("Controls." + control.Name, (PropertyNotificationEventArgs)e);
        }

        public override void WriteXml(XmlWriter writer)
        {
            TypeConverter boolConverter = TypeDescriptor.GetConverter(typeof(bool));
            TypeConverter sizeConverter = TypeDescriptor.GetConverter(typeof(Size));
            TypeConverter pointConverter = TypeDescriptor.GetConverter(typeof(Point));

            writer.WriteElementString("Location", pointConverter.ConvertToString(null, System.Globalization.CultureInfo.InvariantCulture, new Point(Left, Top)));
            writer.WriteElementString("Size", sizeConverter.ConvertToString(null, System.Globalization.CultureInfo.InvariantCulture, new Size(Width, Height)));
            if (Rotation != HeliosVisualRotation.None)
            {
                writer.WriteElementString("Rotation", Rotation.ToString());
            }

            writer.WriteElementString("Hidden", boolConverter.ConvertToInvariantString(IsDefaultHidden));
        }

        public override void ReadXml(XmlReader reader)
        {
            TypeConverter boolConverter = TypeDescriptor.GetConverter(typeof(bool));
            TypeConverter sizeConverter = TypeDescriptor.GetConverter(typeof(Size));
            TypeConverter pointConverter = TypeDescriptor.GetConverter(typeof(Point));

            if (reader.Name.Equals("Location"))
            {
                Point location = (Point)pointConverter.ConvertFromString(null, System.Globalization.CultureInfo.InvariantCulture, reader.ReadElementString("Location"));
                Left = location.X;
                Top = location.Y;

                Size size = (Size)sizeConverter.ConvertFromString(null, System.Globalization.CultureInfo.InvariantCulture, reader.ReadElementString("Size"));
                Width = size.Width;
                Height = size.Height;
            }
            if (reader.Name.Equals("Rotation"))
            {
                Rotation = (HeliosVisualRotation)Enum.Parse(typeof(HeliosVisualRotation), reader.ReadElementString("Rotation"));
            }
            if (reader.Name.Equals("Hidden"))
            {
                IsDefaultHidden = (bool)boolConverter.ConvertFromInvariantString(reader.ReadElementString("Hidden"));
                IsHidden = IsDefaultHidden;
            }
        }

    }
}
