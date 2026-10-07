using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Zuhid.Auth.Base;

public sealed class ControllerFolderRouteConvention : IControllerModelConvention
{
    private const string ControllersSegment = "Controllers";

    public void Apply(ControllerModel controller)
    {
        var controllerType = controller.ControllerType.AsType();
        if (controllerType.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Length > 0)
        {
            return;
        }

        var segments = (controllerType.Namespace ?? string.Empty).Split('.');
        var controllersIndex = Array.IndexOf(segments, ControllersSegment);
        if (controllersIndex < 0 || controllersIndex == segments.Length - 1)
        {
            return;
        }

        var prefix = string.Join('/', segments[(controllersIndex + 1)..].Select(s => s.ToLowerInvariant()));
        var template = $"{prefix}/[controller]";

        if (controller.Selectors.Count == 0)
        {
            controller.Selectors.Add(new SelectorModel());
        }

        foreach (var selector in controller.Selectors)
        {
            selector.AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(template));
        }
    }
}
