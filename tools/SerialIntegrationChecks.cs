using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArmStudio.Core;
class SerialCheck {
 static int Main(string[] args) { try { Run(args).GetAwaiter().GetResult(); return 0; } catch(Exception e) { Console.WriteLine(e); return 1; } }
 static async Task Run(string[] args) {
  using(var transport = new SerialRobotTransport()) {
   var cancel = new CancellationTokenSource();
   if(args[1] == "cancel") cancel.CancelAfter(100);
   try { await transport.ConnectAsync(args[0],115200,cancel.Token); }
   catch(OperationCanceledException) { if(args[1] == "cancel" && !transport.IsConnected) { Console.WriteLine("PASS cancel-connect"); return; } throw; }
   catch(TimeoutException) { if(args[1] == "silent" && !transport.IsConnected) { Console.WriteLine("PASS handshake-timeout"); return; } throw; }
   catch(IOException) { if(args[1] == "mismatch" && !transport.IsConnected) { Console.WriteLine("PASS wrong-firmware"); return; } throw; }
   if(args[1] != "basic" && args[1] != "late") throw new Exception("Bad connection accepted");
   if(!transport.IsConnected || transport.CanMove) throw new Exception("Bad capability state");
   try { await transport.ExecuteAsync("MOVE_L 0 0 0 0 0 0 50", CancellationToken.None); throw new Exception("Motion accepted"); } catch(InvalidOperationException) {}
   if(!transport.IsConnected) throw new Exception("Unsupported request disconnected valid session");
   try { await transport.ExecuteAsync("PING", CancellationToken.None); }
   catch(TimeoutException) { if(args[1] == "late" && !transport.IsConnected) { Console.WriteLine("PASS mismatched-sequence-timeout"); return; } throw; }
   if(args[1] == "late") throw new Exception("Wrong response sequence accepted");
   await transport.ExecuteAsync("STATUS", CancellationToken.None);
   await transport.DisconnectAsync();
   if(transport.IsConnected) throw new Exception("Still connected");
   Console.WriteLine("PASS serial-handshake-ping-status-no-motion-disconnect");
  }
 }
}
