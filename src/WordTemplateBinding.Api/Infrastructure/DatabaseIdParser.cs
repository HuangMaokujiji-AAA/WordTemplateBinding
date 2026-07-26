#pragma warning disable CS1591
using WordTemplateBinding.Core.Exceptions;

namespace WordTemplateBinding.Api.Infrastructure;

internal static class DatabaseIdParser
{
    internal static ulong Required(string? value, string name)
    {
        if (!ulong.TryParse(value, out ulong id) || id == 0)
        {
            throw new InvalidDatabaseIdException(name);
        }

        return id;
    }

    internal static ulong? Optional(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name);
}

#pragma warning restore CS1591
