#include <SPI.h>
#include <MFRC522.h>

#define RST_PIN     5     // Chân RST nối với chân 5
#define SS_PIN      53    // Chân SDA (SS) nối với chân 53

MFRC522 mfrc522(SS_PIN, RST_PIN);  // Tạo đối tượng MFRC522

void setup() {
  Serial.begin(115200);     // Khởi tạo Serial với baudrate 9600
  SPI.begin();            // Khởi tạo giao tiếp SPI
  mfrc522.PCD_Init();     // Khởi tạo MFRC522
  //Serial.println("Chương trình đọc thẻ RFID bằng Arduino Mega");
  //Serial.println("Đưa thẻ RFID vào vùng đọc...");
}

void loop() {
  // Kiểm tra nếu có thẻ mới
  if (!mfrc522.PICC_IsNewCardPresent()) {
    return;
  }
  // Đọc thẻ
  if (!mfrc522.PICC_ReadCardSerial()) {
    return;
  }
  // Tạo chuỗi UID dạng thập phân viết liền
  String fullUID = "";
  for (byte i = 0; i < mfrc522.uid.size; i++) {
    if (mfrc522.uid.uidByte[i] < 10) fullUID += "00";
    else if (mfrc522.uid.uidByte[i] < 100) fullUID += "0";
    fullUID += String(mfrc522.uid.uidByte[i]);
  }
  // Lấy 8 chữ số cuối
  String last8Digits = fullUID;
  if (last8Digits.length() > 8) {
    last8Digits = last8Digits.substring(last8Digits.length() - 8);
  }
  Serial.println(last8Digits);
  // Dừng đọc thẻ
  mfrc522.PICC_HaltA();
  delay(1000); // Đợi 1 giây trước khi đọc thẻ mới
}