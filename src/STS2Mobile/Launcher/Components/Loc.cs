using Godot;

namespace STS2Mobile.Launcher.Components;

// Minimal locale-aware string picker (issue #58). Every user-facing string goes
// through one call that returns Korean when the device language is Korean,
// English otherwise: button labels, tabs, toggles and their states, status text,
// placeholders, badges, dialog titles and OK/Cancel labels, as well as sentences.
// A Korean phone should feel like a Korean app, not a translated English one, so
// labels are localized with the established Android / Steam Korean terms, not
// kept in English. Only proper nouns and technical identifiers stay Latin
// (Steam, Slay the Spire 2, BaseLib, mod ids, file and folder names).
// Log and console lines (PatchHelper.Log, AppendLog diagnostics) stay English:
// they exist for bug reports. Terms, style and the particle table live in
// docs/ko-glossary.md; check a new string against it before adding it.
public static class Loc
{
    private static bool? _isKo;

    public static bool IsKo
    {
        get
        {
            if (_isKo == null)
            {
                try
                {
                    var lang = OS.GetLocaleLanguage();
                    _isKo = !string.IsNullOrEmpty(lang) && lang.StartsWith("ko");
                }
                catch
                {
                    _isKo = false;
                }
            }
            return _isKo.Value;
        }
    }

    public static string Tr(string ko, string en) => IsKo ? ko : en;

    // English file count with correct singular/plural ("1 file", "3 files").
    // Only meant for the English half of a Loc.Tr pair.
    public static string Files(int n) => n == 1 ? "1 file" : $"{n} files";
}
