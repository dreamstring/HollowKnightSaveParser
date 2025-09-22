// BackupTagManager.cs

using System.IO;

public class BackupTagManager
{
    private readonly string _tagFilePath;
    private readonly Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, string>? TagChanged;

    public BackupTagManager(string directory)
    {
        _tagFilePath = Path.Combine(directory, ".backup_tags.json");
        LoadTags();
    }

    private void LoadTags()
    {
        if (!File.Exists(_tagFilePath)) return;
        try
        {
            var json = File.ReadAllText(_tagFilePath);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(json);
            if (dict != null)
            {
                _tags.Clear();
                foreach (var kv in dict)
                    _tags[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取标签文件失败: " + ex);
        }
    }

    private void SaveTags()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_tags, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_tagFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("保存标签失败: " + ex);
        }
    }

    public string? GetTag(string fileName)
    {
        // fileName 应该只是文件名（不带路径）
        _tags.TryGetValue(fileName, out var tag);
        return tag;
    }

    public async Task SetTagAsync(string fileName, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            _tags.Remove(fileName);
        else
            _tags[fileName] = tag.Trim();

        SaveTags();
        await Task.CompletedTask;
        RaiseTagChanged(fileName, tag);
    }

    public async Task RemoveTagAsync(string fileName)
    {
        if (_tags.Remove(fileName))
        {
            SaveTags();
            await Task.CompletedTask;
            RaiseTagChanged(fileName, "");
        }
    }

    private void RaiseTagChanged(string fileName, string newTag)
    {
        System.Diagnostics.Debug.WriteLine($"[TagChanged Raise] {fileName} -> {newTag}");
        try
        {
            TagChanged?.Invoke(fileName, newTag);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TagChanged 事件回调异常: " + ex);
        }
    }
}
