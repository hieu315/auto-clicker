# SwiftMacro Pro

Chạy bản thử mới nhất `SwiftMacroPro-headless-v2.exe` trong thư mục repo sau khi đóng bản cũ đang mở. Có thể build bản chính bằng `build.bat` sau khi đóng app. Chế độ **4. Chrome ẩn** cần Chrome và Node.js 22 trở lên (`node --version`). Chế độ này mở một phiên Chrome headless riêng, không hiện cửa sổ và không gửi input tới chuột Windows. Không có giới hạn tốc độ click; tốc độ thực tế phụ thuộc thời gian xử lý của Chrome, trang web và máy.

## Thử chế độ Chrome ẩn

1. Ghi macro trên trang web như bình thường và điền URL của trang.
2. Đặt **Gốc web X/Y** bằng tọa độ màn hình của góc trên trái *vùng nội dung trang* lúc ghi. Di chuột tới góc đó rồi đọc tọa độ ở dòng `Con trỏ: X/Y` trong app. Ví dụ, điểm click ghi ở `(500, 300)` và gốc web là `(100, 120)` thì Chrome ẩn sẽ click tại `(400, 180)` trong trang.
3. Đặt **Khung W/H** bằng kích thước vùng nội dung trang lúc ghi. Chrome ẩn sẽ tự mở rộng khung nếu macro có điểm click nằm ngoài kích thước này. Giữ cùng mức zoom và kích thước trang để các click theo tọa độ vẫn đúng.
4. Nếu trang cần đăng nhập, bấm **Mở Chrome để đăng nhập**, đăng nhập trong cửa sổ riêng đó rồi đóng hết cửa sổ này. Sau đó chọn **4. Chrome ẩn** và bấm Play. Phiên đăng nhập được lưu ở `%LOCALAPPDATA%\SwiftMacroPro\ChromeProfile`, ngoài repository.
5. Dùng nút Stop hoặc phím tắt dừng như trước.

Macro dùng tọa độ màn hình nên cần gốc web chính xác. Trang thay bố cục, cuộn, zoom hoặc kích thước khung có thể làm click lệch. Chế độ ẩn không làm giảm công việc mà trang phải xử lý khi macro click rất nhanh.

Chạy bài thử trình duyệt cục bộ: `node browser/smoke.js`.
