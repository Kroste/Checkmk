using System.Text;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;

namespace Checkmk.App.Services;

/// <summary>
/// NLog-Layout-Renderer <c>${masked}</c>: rendert die formatierte Log-Message,
/// aber mit maskierten Secrets. In nlog.config statt <c>${message}</c> verwenden.
/// </summary>
[LayoutRenderer("masked")]
[ThreadAgnostic]
public sealed class MaskedLayoutRenderer : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        => builder.Append(SecretMasker.Apply(logEvent.FormattedMessage));
}
