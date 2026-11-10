/*
 * ArmStudio 1 - USB serial diagnostic firmware.
 * Arduino Uno/Nano compatible. NO motor, servo, relay, or PWM output.
 * Select the board and port in Arduino IDE, upload, close Serial Monitor,
 * then connect ArmStudio at 115200 baud, 8N1.
 */
#include <Arduino.h>
#include <string.h>

static const size_t MAX_LINE = 96;
static char inputLine[MAX_LINE];
static size_t inputLength = 0;
static bool discardLine = false;

static bool validSequence(const char *sequence) {
  if (!sequence || sequence[0] != 'N' || sequence[1] == '\0') return false;
  size_t count = 0;
  for (size_t index = 1; sequence[index]; ++index) {
    if (sequence[index] < '0' || sequence[index] > '9') return false;
    if (++count > 18) return false;
  }
  return true;
}

static void reply(const char *prefix, const char *sequence, const char *message) {
  Serial.print(prefix);
  Serial.print(' ');
  Serial.print(sequence);
  Serial.print(' ');
  Serial.print(message);
  Serial.print('\n');
}

static void processLine(char *line) {
  if (strcmp(line, "HELLO 1") == 0) {
    Serial.print(F("ARMSTUDIO 1 DIAGNOSTIC\n"));
    return;
  }
  char *sequence = strtok(line, " ");
  char *operation = strtok(NULL, " ");
  char *extra = strtok(NULL, " ");
  if (!validSequence(sequence)) {
    reply("ERR", "N0", "BAD_SEQUENCE");
    return;
  }
  if (!operation || extra) {
    reply("ERR", sequence, "BAD_COMMAND");
    return;
  }
  if (strcmp(operation, "PING") == 0) reply("OK", sequence, "PONG");
  else if (strcmp(operation, "STATUS") == 0) reply("OK", sequence, "DIAGNOSTIC");
  else reply("ERR", sequence, "UNSUPPORTED");
}

void setup() {
  Serial.begin(115200);
  // No pinMode()/attach(): this sketch intentionally has no physical outputs.
}

void loop() {
  while (Serial.available() > 0) {
    int incoming = Serial.read();
    if (incoming == '\r') continue;
    if (incoming == '\n') {
      if (discardLine) reply("ERR", "N0", "FRAME_TOO_LONG_OR_INVALID");
      else if (inputLength > 0) {
        inputLine[inputLength] = '\0';
        processLine(inputLine);
      }
      inputLength = 0;
      discardLine = false;
      continue;
    }
    if (discardLine) continue;
    if (incoming < 32 || incoming > 126 || inputLength >= MAX_LINE - 1) {
      discardLine = true;
      continue;
    }
    inputLine[inputLength++] = (char)incoming;
  }
}
