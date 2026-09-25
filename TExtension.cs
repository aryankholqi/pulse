using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Pulse;

/// <summary><c>Text="{local:T Layout}"</c> — a live binding to <see cref="Loc"/>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider services) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(services);
}
