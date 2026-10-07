# ⚡ AI Multi-Orchestrator Studio

Ứng dụng Windows Desktop (.NET 10 / WPF) chuyên dụng để **tích hợp và điều phối nhiều AI cùng làm việc**, được thiết kế tối ưu hóa đặc biệt theo các tiêu chí:

1. **Không ngốn dung lượng máy (0 MB lưu trữ mô hình)**: Sử dụng các dịch vụ Cloud API hiệu năng cao (Google Gemini API & Groq Cloud). Máy tính của bạn không cần tải các bộ trọng số mô hình cồng kềnh (thường nặng từ 20GB - 70GB) hay cần card đồ họa khủng, bộ nhớ RAM chỉ tốn ~40-60MB.
2. **Nhiệm vụ chuyên biệt cho từng AI**: Mỗi AI đảm nhận một vai trò độc lập với System Prompt được tinh chỉnh chuyên sâu (Kỹ sư phần mềm, Biên tập viên, Nhà nghiên cứu, Thẩm định viên, Điều phối viên). Bạn có thể tự do thêm bớt hoặc sửa đổi các AI này.
3. **AI chỉ chạy khi đến phần của mình (Lazy Execution)**: AI Router sẽ tự động phân tích câu hỏi của bạn. Chỉ những AI nào được giao nhiệm vụ mới được đánh thức để xử lý. Các AI không liên quan sẽ ở trạng thái `[Nghỉ ngơi / Bỏ qua]`, hoàn toàn không tiêu tốn tài nguyên hay lượt gọi API.

---

## 🚀 Cách khởi chạy ứng dụng

- **Cách 1 (Nhanh nhất)**: Nhấp đúp chuột vào file `Chay_Ung_Dung.bat` trong thư mục này.
- **Cách 2**: Chạy trực tiếp file `AIOrchestrator\bin\Release\net10.0-windows\AIOrchestrator.exe`.

---

## 🔑 Hướng dẫn lấy API Key miễn phí 100%

Ứng dụng hỗ trợ tối đa các dịch vụ có gói miễn phí hào phóng:

1. **Google Gemini API (Khuyên dùng)**:
   - Truy cập: [Google AI Studio](https://aistudio.google.com/apikey)
   - Đăng nhập tài khoản Google và bấm **Create API Key**.
   - Dùng cho: `gemini-2.5-flash`, `gemini-1.5-flash` (tốc độ cao, ngữ cảnh lớn, miễn phí hàng ngày).

2. **Groq Cloud API (Siêu tốc độ)**:
   - Truy cập: [Groq Console](https://console.groq.com/keys)
   - Đăng nhập và tạo API Key miễn phí.
   - Dùng cho: `llama-3.3-70b-versatile`, `llama-3.1-8b-instant` (tốc độ ~300 tokens/s).

3. **Cài đặt vào ứng dụng**:
   - Mở ứng dụng, bấm nút **⚙️ Cài Đặt API Keys** ở góc trên bên phải.
   - Dán API Key vào và bấm **Lưu Cài Đặt**. Cấu hình sẽ được lưu trữ an toàn trong máy bạn.

---

## 🤖 Danh sách các AI Chuyên Dụng Mặc Định

| Biểu tượng | Tên AI | Vai trò chuyên biệt | Nền tảng mặc định |
| :---: | :--- | :--- | :--- |
| 🎯 | **AI Router (Điều phối viên)** | Phân tích yêu cầu, chia việc và chỉ đánh thức AI cần thiết | Gemini 2.5 Flash |
| 💻 | **AI Kỹ sư phần mềm (Coder)** | Viết code chuẩn, tối ưu giải thuật, debug và hướng dẫn | Gemini 2.5 Flash |
| ✍️ | **AI Biên tập & Viết lách (Writer)** | Sáng tạo nội dung, trau chuốt câu từ, bài viết hấp dẫn | Groq Llama 3.3 70B |
| 🔍 | **AI Nhà nghiên cứu (Researcher)** | Phân tích logic, so sánh ưu/nhược điểm, dữ liệu chuyên sâu | Groq Llama 3.3 70B |
| 🛡️ | **AI Thẩm định & Tối ưu (Reviewer)** | Kiểm tra lỗi, chuẩn hóa kết quả đầu ra Markdown đẹp mắt | Gemini 2.5 Flash |

*Bạn hoàn toàn có thể bấm **+ Thêm AI** hoặc bấm biểu tượng ✏️ để tùy biến bất kỳ con AI nào theo ý bạn.*

---

## ⚙️ Các Chế Độ Hoạt Động

1. **🧠 Router Tự Động (Khuyên dùng)**:
   - Bạn chỉ cần nhập bất kỳ câu hỏi nào.
   - Router AI sẽ phân tích và lập kế hoạch: chỉ giao việc cho các AI phù hợp.
   - Ví dụ: Nếu hỏi một thuật toán, chỉ có Coder và Reviewer thức dậy; Writer và Researcher sẽ ở trạng thái `[Bỏ qua]`.
2. **⛓️ Chuỗi Tuần Tự (Custom Pipeline)**:
   - Chạy tuần tự qua các AI đang được tích chọn (từ trên xuống dưới).
   - Kết quả của AI trước sẽ tự động được chuyển tiếp làm ngữ cảnh cho AI sau.
3. **💬 Hỏi 1 AI Chuyên Biệt**:
   - Chọn trực tiếp 1 AI bạn muốn nói chuyện (ví dụ chỉ hỏi Coder AI), các AI khác hoàn toàn không chạy.

## 💬 Chat người dùng với quản trị viên

Tính năng chat cần chạy riêng `AIOrchestrator.ChatServer`; app WPF không tự khởi chạy backend.

1. Trên máy chủ, đặt khóa quản trị dài ít nhất 32 ký tự qua biến môi trường `Chat__AdminKey` (không lưu khóa trong source code), rồi chạy:

   ```powershell
   $env:Chat__AdminKey = "<chuỗi bí mật ngẫu nhiên dài ít nhất 32 ký tự>"
   $env:Chat__DataPath = "C:\VinhAI\data\chat.json"
   $env:ASPNETCORE_URLS = "http://localhost:5080"
   dotnet run --project .\AIOrchestrator.ChatServer\AIOrchestrator.ChatServer.csproj
   ```

   `Chat__DataPath` nên trỏ tới nơi có sao lưu định kỳ. URL HTTP chỉ dùng cho thử nghiệm trên cùng máy; khi cho phép kết nối qua mạng/Internet, phải đặt HTTPS bằng chứng chỉ hợp lệ hoặc reverse proxy HTTPS.

2. Trong **Cài Đặt API Keys** của bản cài đặt quản trị, nhập URL chat server và `Chat__AdminKey`. Trên máy người dùng chỉ cần nhập URL server, không nhập khóa quản trị.
3. Mở **Quản trị → Tin nhắn**, nhập tên người dùng và chọn **Tạo mã truy cập**. Gửi mã hiện ra cho người đó qua kênh riêng; mã chỉ được hiển thị một lần.
4. Người dùng đăng nhập ứng dụng, chọn **Liên hệ admin**, nhập mã và kết nối. Hộp thư quản trị tự làm mới, hiển thị số tin chưa đọc và phát âm báo khi có tin mới.

Phiên đăng nhập chat đang lưu trong bộ nhớ backend; sau khi khởi động lại server, người dùng cần kết nối lại bằng mã đã được cấp. Nội dung tin nhắn và mã băm được lưu trong tệp dữ liệu.

## 👤 Lịch sử và trợ lý riêng theo tài khoản

Mỗi tài khoản Vinh-AI có thư mục lịch sử riêng trong `%LOCALAPPDATA%\AIOrchestrator\conversations\accounts`; danh sách, tìm kiếm, mở, đổi tên và xóa lịch sử chỉ thao tác trong thư mục tài khoản đang đăng nhập. Khi đăng xuất, màn hình hội thoại được xóa trước khi tài khoản khác có thể đăng nhập. Lịch sử cũ được tạo trước khi có phân vùng tài khoản không có thông tin xác định chủ sở hữu, vì vậy không tự nhập vào tài khoản bất kỳ.

Ứng dụng bổ sung trợ lý văn phòng, biên tập kịch bản/storyboard video, thiết kế nội dung PowerPoint theo từng slide, phân tích hình ảnh/đồ vật và **Trợ lý riêng của bạn**. Chế độ hỏi trực tiếp mặc định chọn trợ lý riêng; các AI khác cũng gọi người dùng bằng tên tự nhiên khi phù hợp. Trợ lý sử dụng tối đa 6 cuộc trò chuyện gần nhất của chính tài khoản (câu hỏi và ngữ cảnh trả lời rút gọn) để tiếp tục công việc mà không cần lặp lại toàn bộ bối cảnh. Phần ghi nhớ này được gửi cùng yêu cầu tới nhà cung cấp AI đang xử lý. Dữ liệu lịch sử lưu cục bộ dạng JSON, không được mã hóa bởi tính năng này; người dùng chung tài khoản Windows có thể truy cập tệp ở cấp hệ điều hành.

AI video hiện hỗ trợ ý tưởng, kịch bản, lời dẫn, storyboard và kế hoạch dựng; AI PowerPoint soạn dàn ý, nội dung từng slide và ghi chú thuyết trình. Hai vai trò này chưa xuất tệp video hoặc `.pptx` tự động.

Nếu Groq trả lỗi giới hạn tốc độ (HTTP 429), ứng dụng sẽ thử chuyển riêng AI đó sang Gemini khi đã cấu hình Gemini key. Nếu một AI vẫn lỗi, hệ thống sẽ đánh dấu AI đó và tiếp tục các AI còn lại; kết quả từ AI thành công vẫn được giữ lại. Khi khởi động ứng dụng hoặc đổi tài khoản, trạng thái Done/Error cũ được đặt lại để không nhầm lỗi lần chạy trước với lỗi hiện tại.

## 📎 Đính kèm tệp cho AI

Trong ô yêu cầu, chọn **📎 Đính kèm tệp/ảnh** để thêm tối đa 8 tệp (mỗi tệp tối đa 10 MB, tổng tối đa 20 MB). Ở ô **Gửi Phản Hồi**, nút **📎** cũng mở hộp chọn để đính kèm tệp; nút **📋** dán ảnh từ Clipboard. Ứng dụng trích xuất văn bản từ TXT/Markdown/CSV/JSON/XML, mã nguồn và tài liệu Office Open XML (`.docx`, `.xlsx`, `.pptx`); hình PNG/JPG/WEBP và PDF được gửi tới Gemini để đọc nội dung trực quan. Cần cấu hình Google AI Studio API key để phân tích ảnh/PDF. Nếu mô hình Gemini đang quá tải, ứng dụng tự thử lại rồi chuyển sang mô hình Gemini dự phòng. Có thể bỏ từng tệp bằng nút **×** trước khi gửi.

## 🖼️ Tạo ảnh, đồ thị và thiết kế

Hai nút **📊 Vẽ đồ thị** và **🎨 Thiết kế đồ họa** dùng Pollinations AI (`image.pollinations.ai`) để tạo ảnh, không cần Gemini API key cho bước tạo ảnh. Ứng dụng tải ảnh về máy và hiển thị trực tiếp cùng nội dung trong tab **Kết Quả Cuối Cùng**; câu trả lời thân thiện, không hiện mã lệnh tạo ảnh. Pollinations nhận mô tả yêu cầu để xử lý; chế độ `private=true` được gửi theo API. Nếu có đính kèm ảnh/PDF, Gemini vẫn được dùng để đọc tệp trước khi gửi mô tả đã xử lý tới Pollinations, vì vậy việc phân tích loại đính kèm đó vẫn cần Google AI Studio key.

Trong câu trả lời AI, cú pháp Markdown `![mô tả](https://...)` cũng được hiển thị thành ảnh có kích thước phù hợp, thay vì hiện nội dung dưới dạng mã.

## 🔄 Cập nhật ứng dụng qua GitHub

Ứng dụng mặc định kiểm tra manifest tại `https://github.com/DoCongVinh/VinhAI/releases/latest/download/update.json` khi mở và mỗi 6 giờ. Khi có bản mới, ứng dụng hỏi người dùng trước; chỉ khi chọn đồng ý mới tải, xác minh SHA-256, thay thế tệp ứng dụng và khởi động lại. Người dùng có thể xóa URL manifest trong **Cài đặt → Cập nhật ứng dụng** để tắt kiểm tra.

Các bản phát hành được tạo từ GitHub Actions. Sau khi đẩy mã nguồn lên `main`, tạo và đẩy tag phiên bản mới:

```powershell
git tag v1.0.4
git push origin v1.0.4
```

Workflow kiểm thử, publish một tệp `VinhAI.exe` tự chứa cho `win-x64`, tạo `update.json` có hash SHA-256 và đăng cả hai vào GitHub Release. Để phát hành bản tiếp theo, dùng tag mới lớn hơn bản hiện tại (ví dụ `v1.0.5`). Máy đang chạy bản 1.0.2 trở về trước cần nhập URL manifest ở trên một lần trong cài đặt để nhận bản cập nhật đầu tiên; các bản phát hành mới có URL mặc định.

## 📡 Triển khai ChatServer để chat trực tuyến

GitHub lưu mã nguồn và file phát hành, không chạy API chat. `AIOrchestrator.ChatServer` cần được triển khai riêng lên dịch vụ hỗ trợ Docker, có HTTPS công khai và ổ đĩa bền vững. Có thể dùng Render (chọn Docker, mount persistent disk tại `/var/data`) hoặc VPS của bạn. Cấu hình biến môi trường trên máy chủ:

- `Chat__AdminKey`: chuỗi ngẫu nhiên dài ít nhất 32 ký tự; đặt trong secret/environment settings, không commit vào Git.
- `Chat__DataPath`: `/var/data/chat.json` nếu mount ổ đĩa theo hướng dẫn trên.
- `ASPNETCORE_HTTP_PORTS`: `8080` (container lắng nghe cổng này; nền tảng cung cấp HTTPS bên ngoài).

Khi có URL HTTPS của dịch vụ, nhập URL vào ứng dụng quản trị và các máy người dùng. Khóa quản trị chỉ cấu hình trên máy admin; người dùng kết nối bằng mã truy cập do admin tạo. Không dùng ổ đĩa tạm thời của gói hosting vì dữ liệu chat có thể mất khi khởi động lại.
