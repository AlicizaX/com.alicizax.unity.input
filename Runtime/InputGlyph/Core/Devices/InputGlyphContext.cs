#if INPUTSYSTEM_SUPPORT
using System;

public sealed class InputGlyphContext : IEquatable<InputGlyphContext>
{
    public readonly int DeviceId;
    public readonly int VendorId;
    public readonly int ProductId;
    public readonly string ProfileId;
    public readonly string ControlScheme;
    public readonly string DeviceName;
    public readonly string Layout;
    public readonly string InterfaceName;
    public readonly string Manufacturer;
    public readonly string Product;

    public InputGlyphContext(
        int deviceId,
        int vendorId,
        int productId,
        string profileId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product)
    {
        DeviceId = deviceId;
        VendorId = vendorId;
        ProductId = productId;
        ProfileId = profileId ?? string.Empty;
        ControlScheme = controlScheme ?? string.Empty;
        DeviceName = deviceName ?? string.Empty;
        Layout = layout ?? string.Empty;
        InterfaceName = interfaceName ?? string.Empty;
        Manufacturer = manufacturer ?? string.Empty;
        Product = product ?? string.Empty;
    }

    public bool Equals(InputGlyphContext other)
    {
        return other != null
               && DeviceId == other.DeviceId
               && VendorId == other.VendorId
               && ProductId == other.ProductId
               && InputGlyphStringUtility.EqualsOrdinal(ProfileId, other.ProfileId)
               && InputGlyphStringUtility.EqualsOrdinal(ControlScheme, other.ControlScheme)
               && InputGlyphStringUtility.EqualsOrdinal(DeviceName, other.DeviceName)
               && InputGlyphStringUtility.EqualsOrdinal(Layout, other.Layout)
               && InputGlyphStringUtility.EqualsOrdinal(InterfaceName, other.InterfaceName)
               && InputGlyphStringUtility.EqualsOrdinal(Manufacturer, other.Manufacturer)
               && InputGlyphStringUtility.EqualsOrdinal(Product, other.Product);
    }

}
#endif
