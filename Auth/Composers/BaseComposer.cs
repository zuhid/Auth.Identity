namespace Zuhid.Auth.Composers;

public abstract class BaseComposer(string templateDir = "Composers")
{
    protected virtual async Task<string> ReadTemplate(string filePath)
    {
        return await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, templateDir, filePath));
    }

    protected virtual async Task<string> CreateHtmlAsync(string body, string style = "")
    {
        var combinedStyle = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Composers", "BaseComposer.css")) + style;
        var baseBody = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Composers", "BaseComposer.html"));
        return baseBody
            .Replace("{{style}}", combinedStyle)
            .Replace("{{body}}", body);
    }
}
