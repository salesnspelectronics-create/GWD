namespace GenericWeb.DeploymentManager;

public static class ProductInfo
{
    public static string Version => typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string MajorVersion => Version.Split('.')[0];
    public static string DisplayVersion => "V" + MajorVersion;
}
