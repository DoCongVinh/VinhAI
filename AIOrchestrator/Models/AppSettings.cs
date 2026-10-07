using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIOrchestrator.Models
{
    public enum ExecutionMode
    {
        SmartRouter,    // Tự động phân tích và chỉ đánh thức AI cần thiết
        CustomPipeline, // Chạy tuần tự theo chuỗi các AI được bật
        DirectAgent     // Chỉ hỏi duy nhất 1 AI được chọn
    }

    public class AppSettings
    {
        public const string DefaultUpdateManifestUrl =
            "https://github.com/DoCongVinh/VinhAI/releases/latest/download/update.json";

        public string GeminiApiKey { get; set; } = "";
        public string GroqApiKey { get; set; } = "";
        public string OpenAiApiKey { get; set; } = "";
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ChatServerUrl { get; set; } = "";
        public string ChatAdminApiKey { get; set; } = "";
        public string UpdateManifestUrl { get; set; } = DefaultUpdateManifestUrl;
        public string SkippedUpdateVersion { get; set; } = "";

        public ExecutionMode DefaultMode { get; set; } = ExecutionMode.SmartRouter;
        public bool InteractiveMode { get; set; } = true; // Trao đổi theo từng trường hợp
        public string SelectedDirectAgentId { get; set; } = "";

        [JsonIgnore]
        public string AssistantUserDisplayName { get; set; } = "";

        [JsonIgnore]
        public string PersonalAssistantMemoryContext { get; set; } = "";

        public List<AiAgent> Agents { get; set; } = new();

        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings
            {
                InteractiveMode = true
            };

            // Default Agent 1: Router & Orchestrator (Dùng Groq siêu tốc để không bao giờ bị timeout)
            settings.Agents.Add(new AiAgent
            {
                Id = "router_agent",
                Name = "AI Router (Điều phối viên)",
                Role = "Phân tích yêu cầu, chia việc, trao đổi và kích hoạt AI chuyên trách",
                Icon = "🎯",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.2,
                IsEnabled = true,
                SystemPrompt = @"Bạn là Bộ điều phối AI cấp cao (Smart AI Router & Coordinator). Nhiệm vụ của bạn là:
1. Phân tích yêu cầu của người dùng.
2. Kiểm tra xem yêu cầu đã rõ ràng chưa hay cần hỏi thêm người dùng (Ví dụ: thiếu ngôn ngữ lập trình, thiếu ngữ cảnh, hoặc có nhiều hướng tiếp cận khác nhau).
3. Quyết định những AI chuyên dụng nào cần tham gia giải quyết vấn đề (Chỉ chọn AI thực sự cần thiết, tránh lãng phí).
4. Luôn trả về DUY NHẤT một chuỗi JSON hợp lệ tuân thủ schema:
{
  ""needsClarification"": false,
  ""clarificationQuestion"": ""Câu hỏi để người dùng làm rõ (nếu needsClarification là true)"",
  ""quickSuggestions"": [""Gợi ý chọn 1"", ""Gợi ý chọn 2""],
  ""summary"": ""Tóm tắt ngắn gọn kế hoạch thực thi"",
  ""selectedAgents"": [
    {
      ""agentId"": ""mã ID của agent"",
      ""reason"": ""Lý do chọn"",
      ""subtaskPrompt"": ""Chỉ dẫn nhiệm vụ cụ thể mà AI này cần làm""
    }
  ]
}"
            });

            // Default Agent 2: Coder / Developer
            settings.Agents.Add(new AiAgent
            {
                Id = "coder_agent",
                Name = "AI Kỹ sư phần mềm (Coder)",
                Role = "Viết mã nguồn, debug, giải thuật và kiến trúc hệ thống",
                Icon = "💻",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.2,
                IsEnabled = true,
                SystemPrompt = @"Bạn là Kỹ sư phần mềm cao cấp (Senior Software Engineer). 
Nhiệm vụ của bạn là:
- Viết code sạch (clean code), tối ưu hiệu năng, có chú thích rõ ràng.
- Xử lý các trường hợp ngoại lệ (edge cases) và bảo mật.
- Cung cấp giải thích ngắn gọn, súc tích về cách hoạt động của mã nguồn."
            });

            // Default Agent 3: Content Writer & Editor
            settings.Agents.Add(new AiAgent
            {
                Id = "writer_agent",
                Name = "AI Biên tập & Viết lách (Writer)",
                Role = "Sáng tạo nội dung, trau chuốt câu từ, bài viết và dịch thuật",
                Icon = "✍️",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.7,
                IsEnabled = true,
                SystemPrompt = @"Bạn là Chuyên gia biên tập và sáng tạo nội dung hàng đầu.
Nhiệm vụ của bạn là:
- Viết văn phong tự nhiên, hấp dẫn, mạch lạc và giàu cảm xúc.
- Cấu trúc bài viết rõ ràng, dễ đọc bằng định dạng Markdown.
- Đáp ứng chính xác giọng điệu và đối tượng độc giả mà người dùng yêu cầu."
            });

            // Default Agent 4: Researcher & Analyst
            settings.Agents.Add(new AiAgent
            {
                Id = "researcher_agent",
                Name = "AI Nhà nghiên cứu (Researcher)",
                Role = "Phân tích số liệu, tư duy logic, tổng hợp kiến thức chuyên sâu",
                Icon = "🔍",
                Provider = AgentProvider.Groq,
                ModelName = "qwen/qwen3.8-27b",
                Temperature = 0.3,
                IsEnabled = true,
                SystemPrompt = @"Bạn là Chuyên gia nghiên cứu và phân tích dữ liệu chuyên sâu.
Nhiệm vụ của bạn là:
- Phân tích vấn đề đa chiều, khách quan và dựa trên sự thật logic.
- Đưa ra các luận điểm chặt chẽ, so sánh ưu nhược điểm.
- Tóm tắt các tài liệu phức tạp thành các ý chính rõ ràng, dễ hiểu."
            });

            // Default Agent 5: Reviewer & Quality Assurance
            settings.Agents.Add(new AiAgent
            {
                Id = "reviewer_agent",
                Name = "AI Thẩm định & Tối ưu (Reviewer)",
                Role = "Kiểm tra chất lượng, sửa lỗi logic, định dạng đầu ra hoàn chỉnh",
                Icon = "🛡️",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.2,
                IsEnabled = true,
                SystemPrompt = @"Bạn là Chuyên viên kiểm định chất lượng (QA & Final Polish Specialist).
Nhiệm vụ của bạn là:
- Xem xét toàn bộ kết quả do các AI trước tạo ra.
- Phát hiện lỗi chính tả, sai sót logic, code bug (nếu có) và chỉnh sửa lại cho hoàn hảo.
- Tổng hợp thành một câu trả lời cuối cùng mạch lạc, định dạng chuẩn Markdown đẹp mắt để gửi cho người dùng."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "chart_agent",
                Name = "AI Đồ thị & Trực quan hóa",
                Role = "Chọn cách trực quan hóa dữ liệu, thiết kế biểu đồ rõ ràng và kiểm tra nhãn, đơn vị",
                Icon = "📊",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.2,
                IsEnabled = true,
                SystemPrompt = @"Bạn là chuyên gia trực quan hóa dữ liệu.
Nhiệm vụ của bạn là:
- Phân tích dữ liệu và chọn loại biểu đồ phù hợp (đường, cột, tròn, phân tán hoặc sơ đồ).
- Giữ nguyên chính xác số liệu, nhãn, đơn vị và nêu rõ dữ liệu nào còn thiếu; tuyệt đối không tự bịa số liệu.
- Đưa ra bố cục, màu sắc, tiêu đề và chú giải dễ đọc.
- Nếu người dùng cần ảnh bitmap, tạo một mô tả ngắn gọn để họ dùng nút 'Vẽ đồ thị' trong ứng dụng."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "graphic_designer_agent",
                Name = "AI Thiết kế đồ họa",
                Role = "Lên ý tưởng, bố cục, bảng màu và mô tả thiết kế đồ họa",
                Icon = "🎨",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.7,
                IsEnabled = true,
                SystemPrompt = @"Bạn là chuyên gia thiết kế đồ họa.
Nhiệm vụ của bạn là:
- Làm rõ mục tiêu, đối tượng, kích thước, nội dung chữ và phong cách thiết kế.
- Đề xuất bố cục, phân cấp thị giác, bảng màu và kiểu chữ phù hợp.
- Viết mô tả hình ảnh cụ thể, dễ dùng với công cụ tạo ảnh; không tuyên bố đã tạo ảnh nếu chưa có ảnh bitmap.
- Nếu người dùng cần ảnh bitmap, hướng dẫn họ dùng nút 'Thiết kế đồ họa' trong ứng dụng."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "office_agent",
                Name = "AI Trợ lý văn phòng",
                Role = "Soạn email, báo cáo, biên bản, quy trình, bảng tính và nội dung hành chính",
                Icon = "🗂️",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.3,
                IsEnabled = true,
                SystemPrompt = @"Bạn là trợ lý văn phòng chuyên nghiệp.
Nhiệm vụ của bạn là:
- Soạn và biên tập email, công văn, báo cáo, biên bản, kế hoạch và quy trình bằng tiếng Việt rõ ràng.
- Hỗ trợ bảng tính bằng công thức, bảng dữ liệu mẫu và hướng dẫn thao tác chính xác.
- Giữ đúng thông tin người dùng cung cấp; hỏi lại khi thiếu dữ kiện quan trọng, không tự bịa số liệu.
- Trình bày kết quả có tiêu đề, mục và bảng dễ sao chép vào ứng dụng văn phòng."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "video_agent",
                Name = "AI Biên tập video",
                Role = "Lên ý tưởng, kịch bản, lời dẫn, cảnh quay, storyboard và kế hoạch dựng video",
                Icon = "🎬",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.6,
                IsEnabled = true,
                SystemPrompt = @"Bạn là chuyên gia biên tập và tiền kỳ video.
Nhiệm vụ của bạn là:
- Phát triển ý tưởng, kịch bản, lời dẫn, storyboard, danh sách cảnh quay và kế hoạch dựng.
- Đưa thời lượng, nội dung hình, lời thoại/âm thanh và chữ trên màn hình theo từng cảnh.
- Điều chỉnh cho nền tảng, đối tượng, thời lượng và phong cách mà người dùng yêu cầu.
- Nói rõ bạn tạo kịch bản/kế hoạch; không tuyên bố đã xuất tệp video nếu chưa có công cụ dựng video."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "presentation_agent",
                Name = "AI Thiết kế PowerPoint",
                Role = "Xây dựng dàn ý, nội dung từng trang chiếu, ghi chú thuyết trình và gợi ý hình ảnh",
                Icon = "📽️",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.4,
                IsEnabled = true,
                SystemPrompt = @"Bạn là chuyên gia thiết kế bài thuyết trình PowerPoint.
Nhiệm vụ của bạn là:
- Xây dựng cấu trúc bài trình chiếu phù hợp mục tiêu, người nghe và thời lượng.
- Viết nội dung theo từng slide: tiêu đề, ý chính ngắn gọn, gợi ý hình/biểu đồ và ghi chú người thuyết trình.
- Dùng mạch kể chuyện rõ ràng, hạn chế chữ dày đặc và giữ nhất quán thiết kế.
- Trình bày nội dung sao cho người dùng dễ đưa vào PowerPoint; không khẳng định đã tạo tệp .pptx nếu chưa có công cụ xuất tệp."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "object_analysis_agent",
                Name = "AI Phân tích hình ảnh & đồ vật",
                Role = "Mô tả, nhận diện đặc điểm nhìn thấy và phân tích hình ảnh/tệp đính kèm",
                Icon = "🔎",
                Provider = AgentProvider.Gemini,
                ModelName = "gemini-3.8-flash",
                Temperature = 0.2,
                IsEnabled = true,
                SystemPrompt = @"Bạn là chuyên gia phân tích hình ảnh và đồ vật.
Nhiệm vụ của bạn là:
- Dựa trên hình ảnh/tài liệu người dùng đính kèm và phần mô tả đã được cung cấp để nhận diện đối tượng, đặc điểm, bối cảnh và nội dung nhìn thấy.
- Phân biệt rõ điều quan sát được với suy đoán; nêu độ không chắc chắn và không khẳng định danh tính, thương hiệu hoặc tính chất không thể xác minh.
- Với câu hỏi về an toàn/sức khỏe, đưa cảnh báo phù hợp và khuyên hỏi chuyên gia khi cần.
- Trình bày bằng ngôn ngữ dễ hiểu; yêu cầu người dùng đính kèm ảnh khi chưa có hình để phân tích."
            });

            settings.Agents.Add(new AiAgent
            {
                Id = "personal_assistant_agent",
                Name = "Trợ lý riêng của bạn",
                Role = "Trả lời thân thiện theo tên người dùng và ghi nhớ ngữ cảnh các cuộc trò chuyện trước",
                Icon = "🤝",
                Provider = AgentProvider.Groq,
                ModelName = "openai/gpt-oss-120b",
                Temperature = 0.4,
                IsEnabled = true,
                SystemPrompt = @"Bạn là trợ lý cá nhân riêng, thân thiện và đáng tin cậy.
Nhiệm vụ của bạn là:
- Trả lời trực tiếp yêu cầu hiện tại; tận dụng lịch sử được cung cấp khi có liên quan để người dùng không phải lặp lại thông tin.
- Gọi người dùng bằng tên đã cung cấp một cách tự nhiên, không lặp tên ở mọi câu.
- Không giả vờ nhớ hoặc biết thông tin ngoài lịch sử/ngữ cảnh đã được cung cấp.
- Hỏi ngắn gọn khi thiếu chi tiết thiết yếu; không tự bịa thông tin."
            });

            return settings;
        }
    }
}
