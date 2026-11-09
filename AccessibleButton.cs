using System.Windows.Forms;

internal sealed class AccessibleButton : AntdUI.Button
{
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        return new ButtonAccessibleObject(this);
    }

    private sealed class ButtonAccessibleObject : ControlAccessibleObject
    {
        internal ButtonAccessibleObject(AccessibleButton owner) : base(owner) { }

        public override string Name
        {
            get { return ((AccessibleButton)Owner).Text; }
            set { base.Name = value; }
        }

        public override AccessibleRole Role
        {
            get { return AccessibleRole.PushButton; }
        }
    }
}
