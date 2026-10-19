namespace BccCode.Template.Web.Frontend;

public enum FrontendMode
{
    /// <summary>Development: index.html is fetched from the Vite dev server and everything else is proxied to it (YARP).</summary>
    Vite,

    /// <summary>E2E, tests and the container image: index.html and the hashed assets are served from a built frontend.</summary>
    Static,
}

public sealed class FrontendOptions
{
    public FrontendMode Mode { get; set; } = FrontendMode.Static;

    /// <summary>The folder with the built frontend (<c>frontend/dist</c>, or <c>wwwroot</c> in the image).</summary>
    public string? StaticPath { get; set; }
}
