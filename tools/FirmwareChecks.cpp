#include "Arduino.h"
#include <cassert>
#include <iostream>
FakeSerial Serial;
#include "../firmware/ArmStudioDiagnostic/ArmStudioDiagnostic.ino"
static void check(const std::string &input, const std::string &expected) {
  Serial.input = input; Serial.output.clear(); loop(); assert(Serial.output == expected);
}
int main() {
  setup();
  check("HELLO 1\n", "ARMSTUDIO 1 DIAGNOSTIC\n");
  check("N1 PING\r\n", "OK N1 PONG\n");
  check("N2 STATUS\n", "OK N2 DIAGNOSTIC\n");
  check("N3 MOVE_L\n", "ERR N3 UNSUPPORTED\n");
  check("N4 PING EXTRA\n", "ERR N4 BAD_COMMAND\n");
  check("BAD STATUS\n", "ERR N0 BAD_SEQUENCE\n");
  check(std::string(100, 'X') + "\nN6 PING\n", "ERR N0 FRAME_TOO_LONG_OR_INVALID\nOK N6 PONG\n");
  check("N7 PING", "");
  check("\n", "OK N7 PONG\n");
  std::cout << "Arduino sketch: 9 host-stub protocol checks passed.\n";
}
