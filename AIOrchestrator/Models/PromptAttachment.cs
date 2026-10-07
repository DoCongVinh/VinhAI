namespace AIOrchestrator.Models
{
    public sealed class PromptAttachment
    {
        public PromptAttachment(string fullPath)
        {
            FullPath = fullPath;
            FileName = System.IO.Path.GetFileName(fullPath);
            long bytes = new System.IO.FileInfo(fullPath).Length;
            SizeLabel = bytes < 1024 * 1024
                ? $"{bytes / 1024d:F0} KB"
                : $"{bytes / (1024d * 1024d):F1} MB";
        }

        public string FullPath { get; }
        public string FileName { get; }
        public string SizeLabel { get; }
    }
}
