using GptImagePlayground.Core;

namespace GptImagePlayground;

public sealed class LocalizedStrings
{
    public string this[string key] => Text.Get(key);
}
