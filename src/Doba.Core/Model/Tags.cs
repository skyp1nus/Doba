using System.Text;

namespace Doba.Core.Model;

public static class Tags
{
    /// <summary>
    /// Normalises a typed tag name like the prototype's createList: trim, lower-case, strip leading '#',
    /// collapse whitespace runs to '-'. Returns null when nothing is left.
    /// Unlike the prototype, whitespace exposed by stripping '#' ("# deep work") is trimmed too, so the result never
    /// starts or ends with '-' because of it.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var s = raw.Trim().ToLowerInvariant().TrimStart('#').Trim();
        if (s.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder(s.Length);
        var inSpace = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inSpace)
                {
                    sb.Append('-');
                    inSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                inSpace = false;
            }
        }

        return sb.ToString();
    }
}
