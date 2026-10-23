using System.Text;

namespace TaskList.Core;

/// <summary>
/// Owns backlog.md on disk. Every change goes through <see cref="Mutate"/>, which first re-reads the
/// file if someone else changed it, applies the change to the fresh copy, and writes it back. That way
/// an edit made in another editor is never overwritten by a stale in-memory copy.
/// </summary>
public sealed class BacklogStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly object _gate = new();
    private string _lastKnownText = "";
    private DateTime _lastKnownWriteUtc;
    private bool _loaded;

    public string FilePath { get; }
    public BacklogDocument Document { get; private set; } = new();

    /// <summary>Raised after the document changes. The flag is true when the change came from outside the app.</summary>
    public event Action<bool>? Changed;

    public BacklogStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
    }

    /// <summary>Loads the file, creating it from a starter template if it doesn't exist yet.</summary>
    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                WriteText(BacklogMarkdown.Template());
            }
            ReloadFromDisk();
        }
        Changed?.Invoke(true);
    }

    /// <summary>
    /// Checks whether the file changed on disk since we last read or wrote it and reloads if so.
    /// Call this on a timer. Returns true when the document was reloaded.
    /// </summary>
    public bool PollForExternalChanges()
    {
        bool changed;
        lock (_gate)
        {
            changed = HasChangedOnDisk() && ReloadFromDisk();
        }
        if (changed) Changed?.Invoke(true);
        return changed;
    }

    /// <summary>
    /// Applies a change and saves. The action gets the freshest copy of the document and returns
    /// false if there was nothing to change (for example the task it targets no longer exists).
    /// </summary>
    public bool Mutate(Func<BacklogDocument, bool> change)
    {
        lock (_gate)
        {
            if (HasChangedOnDisk()) ReloadFromDisk();
            // Never write over a file we couldn't fully understand.
            if (Document.Errors.Count > 0) return false;
            if (!change(Document)) return false;
            WriteText(BacklogMarkdown.Serialize(Document));
            // Re-parse what we wrote so the in-memory copy matches the file exactly.
            Document = BacklogMarkdown.Parse(_lastKnownText);
        }
        Changed?.Invoke(false);
        return true;
    }

    private bool HasChangedOnDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            if (File.GetLastWriteTimeUtc(FilePath) == _lastKnownWriteUtc) return false;
            return ReadText() != _lastKnownText;
        }
        catch (IOException)
        {
            // Another program is mid-write; try again on the next poll.
            return false;
        }
    }

    /// <summary>Returns true when the content actually differed from what we had.</summary>
    private bool ReloadFromDisk()
    {
        var text = ReadText();
        _lastKnownWriteUtc = File.GetLastWriteTimeUtc(FilePath);
        if (_loaded && text == _lastKnownText) return false;
        _loaded = true;
        _lastKnownText = text;
        Document = BacklogMarkdown.Parse(text);
        return true;
    }

    private string ReadText()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private void WriteText(string text)
    {
        // Write to a temp file and swap it in, so a crash mid-save never leaves half a backlog.
        var temp = FilePath + ".tmp";
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(temp, text, Utf8NoBom);
                File.Move(temp, FilePath, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
        _lastKnownText = text;
        _lastKnownWriteUtc = File.GetLastWriteTimeUtc(FilePath);
    }
}
