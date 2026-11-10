#pragma once
#include <string>
#include <stddef.h>
#define F(x) x
class FakeSerial {
 public:
  std::string input, output;
  void begin(int) {}
  int available() { return (int)input.size(); }
  int read() { if(input.empty()) return -1; int c = (unsigned char)input[0]; input.erase(0,1); return c; }
  void print(char c) { output += c; }
  void print(const char *text) { output += text; }
};
extern FakeSerial Serial;
