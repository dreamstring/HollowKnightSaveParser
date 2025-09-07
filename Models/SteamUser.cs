namespace HollowKnightSaveParser.Models
{
    public class SteamUser
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        
        public override string ToString() => DisplayName;
    }
}