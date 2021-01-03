using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.System.PS2Support.Devices;
using Cosmos.System.Emulation;
using IL2CPU.API.Attribs;
using XSharp;
using XSharp.Assembler;
using System.IO;

namespace Cosmos.Core.TestExec
{
    public class RunExec86: XSharp.Assembler.AssemblerMethod
    {
        unsafe public override void AssembleNew(Assembler aAssembler, object aMethodInfo)
        {
            XS.Jump("[static_field__AerOS_Software_SoftwareExecuter_libTestPTR]");
        }
    }
    public class run
    {
        [PlugMethod(Assembler = typeof(RunExec86))]
        public void Run()
        {
        }
    }
}

namespace AerOS.Software
{
    unsafe static class SoftwareExecuter
    {
        //TEST
        //[ManifestResourceStream(ResourceName = "TestEnvOS.refs.test.rlib")]
        public static byte[] libTest;
        public static byte* libTestPTR;

        unsafe public static void JumpToExec()
        {
            fixed (byte* PTR = libTest)
                libTestPTR = PTR;

            Cosmos.Core.TestExec.run run = new Cosmos.Core.TestExec.run();
            run.Run();
        }
    }
}


namespace TestEnvOS
{
    public class Kernel : Sys.Kernel
    {
        uint X, Y;
        bool isFSloaded = false;
        string host = null;
        string Hostname
        {
            get
            {
                if (!isFSloaded) return "localhost";
                if (host is null)
                {
                    if (!File.Exists(@"0:\System\inf\hostname"))
                    {
                        File.Create(@"0:\System\inf\hostname").Close();
                        File.WriteAllLines(@"0:\System\inf\hostname", new string[] { "localhost" });
                        host = "localhost";
                    }
                    host = File.ReadAllLines(@"0:\System\inf\hostname")[0];
                }
                return host;
            }
            set
            {
                if (!isFSloaded) return;
                if (!File.Exists(@"0:\System\inf\hostname"))
                {
                    File.Create(@"0:\System\inf\hostname").Close();
                    File.WriteAllLines(@"0:\System\inf\hostname", new string[] { value });
                    return;
                }
                File.WriteAllLines(@"0:\System\inf\hostname", new string[] { value });
                host = value;
            }
        }
        Dictionary<string, string> bind = null;
        Dictionary<string, string> UserPass
        {
            get
            {
                if (bind is null)
                {
                    bind = new Dictionary<string, string>();
                    foreach (string file in Directory.GetFiles(@"0:\System\inf"))
                    {
                        string[] userdat = File.ReadAllLines($@"0:\System\inf\{Path.GetFileName(file)}");
                        if (userdat.Length == 0) continue;
                        bind.Add(Path.GetFileNameWithoutExtension(file), userdat[0]);
                        if (userdat.Length < 2)
                        {
                            if (Path.GetFileNameWithoutExtension(file) == "SystemAuthority")
                                File.WriteAllLines($@"0:\System\inf\{Path.GetFileName(file)}", new string[] { userdat[0], "3" });
                            else if (Path.GetFileNameWithoutExtension(file) == "superuser")
                                File.WriteAllLines($@"0:\System\inf\{Path.GetFileName(file)}", new string[] { userdat[0], "1" });
                            else
                                File.WriteAllLines($@"0:\System\inf\{Path.GetFileName(file)}", new string[] { userdat[0], "0" });
                        }
                    }
                    return bind;
                }
                return bind;
            }
        }
        Executable ConstructExecutable(byte[] data, params string[] parameters)
        {
            FGMSECInstructionSet set = new FGMSECInstructionSet();
            Executable exec;
            exec = new Executable(data, set, 3);
            exec.ReadData();
            if (parameters.Length > 0)
            {
                int addr = exec.Memory.Data.Count;
                for (int i = 0; i < parameters.Length; i++)
                {
                    foreach (char c in parameters[i])
                        exec.Memory.AddChar(c);
                    exec.Memory.AddChar(0);
                }
                exec.Memory.Stack.Push(addr);
                exec.Memory.Stack.Push(parameters.Length - 1);
            }
            exec.AddSystemCall((Executable caller) =>
            {
                int addr = (int)((FGMSECInstructionSet)caller.usingInstructionSet).CPU.GetRegData(3);
                char c = (char)caller.Memory.ReadChar(addr);

                while (c != 0)
                {
                    Console.Write(c);
                    addr++;
                    c = (char)caller.Memory.ReadChar(addr);
                }
            });
            exec.AddSystemCall((Executable caller) =>
            {
                string input = Console.ReadLine();
                input += '\0';
                int addr = caller.Memory.Data.Count;
                int caddr = 0;
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if (!caller.Memory.AddChar(c))
                    {
                        caller.Memory.WriteChar(caddr, c);
                        addr = 0;
                        caddr++;
                    }
                }
                ((FGMSECInstructionSet)caller.usingInstructionSet).CPU.SetRegData(3, (uint)addr);
            });
            exec.AddSystemCall((Executable caller) =>
            {
                Console.Clear();
            });
            exec.AddSystemCall((Executable caller) =>
            {
                if (caller.RunningUser.privLevel != services.Usermanager.privilege.User)
                {
                    ((FGMSECInstructionSet)caller.usingInstructionSet).CPU.SetRegData(3, 1);
                    return;
                }
                if (PrivilElev(caller.RunningUser.username))
                {
                    caller.RunningUser = new services.Usermanager.User() { username = "superuser", privLevel = services.Usermanager.privilege.Privileged };
                    ((FGMSECInstructionSet)caller.usingInstructionSet).CPU.SetRegData(3, 1);
                }
                else
                {
                    ((FGMSECInstructionSet)caller.usingInstructionSet).CPU.SetRegData(3, 0);
                }

            });
            exec.AddSystemCall((Executable caller) =>
            {
                var username = caller.RunningUser.username;
                int addr = caller.Memory.Data.Count;
                foreach (char c in username)
                    caller.Memory.AddChar(c);
                caller.Memory.AddChar(0);
                ((FGMSECInstructionSet)caller.usingInstructionSet).CPU.SetRegData(3, (uint)addr);
            });
            return exec;
        }
        bool CheckPasswd(string username)
        {
            if (!UserPass.ContainsKey(username))
            {
                Console.WriteLine("User does not exist!");
                return false;
            }
            Console.Write($"[usermanager] Password for {username}: ");
            var pass = ReadPass();

            return pass == UserPass[username];
        }
        private void ChangePassword(string username)
        {
            if (!UserPass.ContainsKey(username))
            {
                var pass = RegPass();
                AddUser(username, pass);
                bind.Add(username, pass);
                return;
            }
            if (!CheckPasswd(username))
            {
                Console.WriteLine("Wrong password! Try again!");
                return;
            }
            var passwd = RegPass();
            AddUser(username, passwd);
            bind[username] = passwd;
        }
        private void Login()
        {
            if (!Directory.Exists(@"0:\System\inf")) {
                Directory.CreateDirectory(@"0:\System\inf");
                services.Usermanager.CurrentUser = Register();
                AddPrivil(services.Usermanager.CurrentUser.username);
                return;
            }
            Console.Write("Username: ");
            var usr = Console.ReadLine();
            if (!UserPass.ContainsKey(usr))
            {
                Console.WriteLine("Username not found!\nRetry.");
                Login();
                return;
            }
            Console.Write("Password: ");
            var psd = ReadPass();
            if (UserPass[usr] != psd)
            {
                Console.WriteLine("Wrong password!\nRetry.");
                Login();
                return;
            }
            if (int.TryParse(File.ReadAllLines($@"0:\System\inf\{usr}.usr")[1], out int priv))
                services.Usermanager.CurrentUser = new services.Usermanager.User() { username = usr, privLevel = (services.Usermanager.privilege)priv };
            else
            {
                Console.WriteLine("An internal error occurred, for this reason we are loading you in a temporary user");
                services.Usermanager.CurrentUser = new services.Usermanager.User() { username = "temp", privLevel = services.Usermanager.privilege.User };
            }
        }
        private void RunAs(string username, string command)
        {
            if (!UserPass.ContainsKey(username))
            {
                Console.WriteLine("User does not exist");
                return;
            }
            if (services.Usermanager.CurrentUser.privLevel == services.Usermanager.privilege.User)
            {
                Console.Write($"Password for {username}: ");
                var pass = ReadPass();
                if (pass != UserPass[username])
                {
                    Console.WriteLine("Wrong password. Try again.");
                    return;
                }
                services.Usermanager.User old = services.Usermanager.CurrentUser;
                if (int.TryParse(File.ReadAllLines($@"0:\System\inf\{username}.usr")[1], out int priv))
                    services.Usermanager.CurrentUser = new services.Usermanager.User() { username = username, privLevel = (services.Usermanager.privilege)priv };
                else
                {
                    Console.WriteLine($"An error occurred while trying to execute that command as {username}");
                }
                Console.WriteLine($"Forced by: {old.username}");
                RunCommand(command);
                services.Usermanager.CurrentUser = old;
            }
            
        }
        private string ReadPass()
        {
            var password = "";
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                        password = password.Remove(password.Length - 1, 1);
                    continue;
                }
                password += key.KeyChar;
            }
            return password;
        }
        private string RegPass()
        {
            Console.Write("Input a new password: ");
            var password = ReadPass();
            Console.Write("Verify password: ");
            var ver = ReadPass();
            if (ver != password)
            {
                Console.WriteLine("Missmatching passwords! Try again!");
                return RegPass();
            }
            return password;
        }
        private void AddUser(string username, string password)
        {
            if (username == "SystemAuthority")
            {
                Console.WriteLine("SystemAuthority password cannot be modified");
            }
            else if (username == "superuser")
            {
                if (services.Usermanager.CurrentUser.privLevel != services.Usermanager.privilege.User)
                    File.WriteAllLines($@"0:\System\inf\{username}.usr", new string[] { password, "1" });
                else
                    Console.WriteLine("A privileged user cannot be modified/created by a normal user");
            }
            else
                File.WriteAllLines($@"0:\System\inf\{username}.usr", new string[] { password, "0" });
        }
        private services.Usermanager.User Register()
        {
            Console.Write("Input a new username: ");
            var username = Console.ReadLine();
            var passwd = RegPass();
            AddUser(username, passwd);
            if (bind != null)
            {
                bind.Add(username, passwd);
            }
            return new services.Usermanager.User() { username = username, privLevel = services.Usermanager.privilege.User };
        }

        protected override void BeforeRun()
        {
            FGMSECInstructionSet.Install();
            Sys.PS2Support.Global.InitPS2Port();
            X = Sys.MouseManager.X;
            Y = Sys.MouseManager.Y;
            Console.Clear();
            Console.WriteLine("[TestEnvOS 0.1.2 AerOSTeam] - PS2SupportVersion & Usermanager implementation");
        }
        private void Interrupt(ref Cosmos.Core.INTs.IRQContext context)
        {
            Console.WriteLine("INTERRUPT FROM C!");
        }
        private void AddPrivil(string username)
        {
            if (!Directory.Exists(@"0:\System\inf\dat")) Directory.CreateDirectory(@"0:\System\inf\dat");
            if (!File.Exists(@"0:\System\inf\dat\privileged.usr"))
            {
                File.Create(@"0:\System\inf\dat\privileged.usr").Close();
            }
            List<string> dat = new List<string>();
            string[] old = File.ReadAllLines(@"0:\System\inf\dat\privileged.usr");
            foreach (string s in old) dat.Add(s);
            dat.Add(username);
            File.WriteAllLines(@"0:\System\inf\dat\privileged.usr", dat.ToArray());
            Console.WriteLine($"Made {username} a privileged user!");
        }
        private void RemPrivil(string username)
        {
            if (!Directory.Exists(@"0:\System\inf\dat")) Directory.CreateDirectory(@"0:\System\inf\dat");
            if (!File.Exists(@"0:\System\inf\dat\privileged.usr"))
            {
                File.Create(@"0:\System\inf\dat\privileged.usr").Close();
                return;
            }
            List<string> dat = new List<string>();
            string[] old = File.ReadAllLines(@"0:\System\inf\dat\privileged.usr");
            foreach (string s in old) if (s != username) dat.Add(s);
            if (dat.Count == 0)
            {
                Console.WriteLine("At least one privileged user is required!");
                return;
            }
            File.WriteAllLines(@"0:\System\inf\dat\privileged.usr", dat.ToArray());
            Console.WriteLine($"Made {username} no longer a privileged user!");
        }
        private bool PrivilElev(string username)
        {
            if (services.Usermanager.CurrentUser.privLevel != services.Usermanager.privilege.User) return true;
            if (!Directory.Exists(@"0:\System\inf\dat")) Directory.CreateDirectory(@"0:\System\inf\dat");
            if (!services.Usermanager.IsLoaded)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Userspace not initialized");
                Console.ForegroundColor = ConsoleColor.White;
            }
            if (!File.Exists(@"0:\System\inf\dat\privileged.usr"))
            {
                File.Create(@"0:\System\inf\dat\privileged.usr").Close();
                Console.WriteLine($"{username} is not a privileged user!");
                return false;
            }
            bool cont = false;
            foreach (string privil in File.ReadAllLines(@"0:\System\inf\dat\privileged.usr"))
            {
                if (privil == username) { cont = true; break; }
            }
            if (!cont)
            {
                Console.WriteLine($"{username} is not a privileged user!");
                return false;
            }
            Console.Write($"[privil] password for {services.Usermanager.CurrentUser.username}: ");
            var pass = ReadPass();
            if (UserPass[username] != pass)
            {
                Console.WriteLine("Wrong password! Try again!");
                return false;
            }
            return true;
        }
        private void Privil(string cmd)
        {
            if (!isFSloaded)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Filesystem not initialized!\nPlease initialize it with command \"fs load\"");
                Console.ResetColor();
                return;
            }
            if (!PrivilElev(services.Usermanager.CurrentUser.username)) return;
            services.Usermanager.User old = services.Usermanager.CurrentUser;
            services.Usermanager.CurrentUser = new services.Usermanager.User() { username = "superuser", privLevel = services.Usermanager.privilege.Privileged };
            var comm = cmd.Split(' ');
            if (comm[0] == "-a")
            {
                if (comm.Length > 1) { 
                    AddPrivil(comm[1]);
                }
                else Console.WriteLine("Syntax error: privil -a (username)");
            }
            else if (comm[0] == "-r")
            {
                if (comm.Length > 1)
                {
                    RemPrivil(comm[1]);
                }
                else Console.WriteLine("Syntax error: privil -r (username)");
            }
            else RunCommand(cmd);
            services.Usermanager.CurrentUser = old;
        }
        public void TaskRun()
        {
            while (true)
                Sys.Emulation.Threading.TaskManager.Next();
        }
        private void RunCommand(string cmd)
        {
            string[] comm = cmd.Split(' ');
            if (cmd == "info")
            {
                Console.WriteLine("TestEnvOS is the os that will be used by developers of AerOS to test something out before implementing it into the AerOSFramework.\n" +
                    "If you want to add stuff, please consider making a help entry for it.");
            }
            else if (cmd == "cls")
            {
                Console.Clear();
            }
            else if (cmd == "syshalt")
            {
                if (services.Usermanager.CurrentUser == null || services.Usermanager.CurrentUser.privLevel != services.Usermanager.privilege.System)
                {
                    Console.WriteLine("No enough privilege to do that!");
                    return;
                }
                Console.WriteLine("System is going to be halted");
                while (true) ;
            }
            else if (cmd == "powerdown")
            {
                if (services.Usermanager.CurrentUser == null || services.Usermanager.CurrentUser.privLevel != services.Usermanager.privilege.System)
                {
                    Console.WriteLine("No enough privilege to do that!");
                    return;
                }
                Cosmos.Core.ACPI.Shutdown();
            }
            else if (cmd == "reboot")
            {
                if (services.Usermanager.CurrentUser == null || services.Usermanager.CurrentUser.privLevel == services.Usermanager.privilege.User)
                {
                    Console.WriteLine("No enough privilege to do that!");
                    return;
                }
                Sys.Power.Reboot();
            }
            else if (cmd == "hostname")
            {
                Console.WriteLine(Hostname);
            }
            else if (cmd == "hostname -c")
            {
                if (services.Usermanager.CurrentUser == null || services.Usermanager.CurrentUser.privLevel == services.Usermanager.privilege.User)
                {
                    Console.WriteLine("No enough privilege to do that!");
                    return;
                }
                Console.Write("New hostname: ");
                var nHost = Console.ReadLine();
                Hostname = nHost;
            }
            else if (cmd == "login")
            {
                if (!isFSloaded)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Filesystem not initialized!\nPlease initialize it with command \"fs load\"");
                    Console.ResetColor();
                    return;
                }
                Login();
            }
            else if (comm[0] == "privil")
            {
                string ncmd = cmd.Remove(0, 7);
                Privil(ncmd);
            }
            else if (comm[0] == "passwd")
            {
                if (comm.Length > 1)
                {
                    if (services.Usermanager.CurrentUser.privLevel != services.Usermanager.privilege.User)
                        ChangePassword(comm[1]);
                    else
                        Console.WriteLine("No enough privilege to do that!");
                }
                else
                {
                    ChangePassword(services.Usermanager.CurrentUser.username);
                }
            }
            else if (cmd == "register")
            {
                if (!isFSloaded)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Filesystem not initialized!\nPlease initialize it with command \"fs load\"");
                    Console.ResetColor();
                    return;
                }
                Register();
            }
            else if (cmd == "whoami")
            {
                if (!services.Usermanager.IsLoaded)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Userspace not initialized");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else Console.WriteLine(services.Usermanager.CurrentUser.username);
            }
            else if (cmd == "mousepos")
            {
                Console.WriteLine($"X: {Sys.MouseManager.X}\nY: {Sys.MouseManager.Y}");
            }
            else if (cmd == "loadTestProg x86")
            {
                Cosmos.Core.INTs.SetIntHandler(0x14, Interrupt);
                AerOS.Software.SoftwareExecuter.JumpToExec();
            }
            else if (cmd == "fs load")
            {
                if (isFSloaded)
                {
                    Console.WriteLine("Filesystem already loaded!");
                    return;
                }
                var fs = new Sys.FileSystem.CosmosVFS();
                Sys.FileSystem.VFS.VFSManager.RegisterVFS(fs);
                Console.Clear();
                Console.WriteLine("Login");
                Login();
                Console.WriteLine($"Welcome {services.Usermanager.CurrentUser.username}!");
                isFSloaded = true;
                if (!Directory.Exists($@"0:\System\bin")) Directory.CreateDirectory($@"0:\System\bin");
                //Sys.Thread thread = new Sys.Thread(TaskRun);
                //thread.Start();
            }
            else if(comm[0] == "as")
            {
                if (comm.Length < 2)
                {
                    Console.WriteLine("Syntax error.");
                    return;
                }
                RunAs(comm[1], comm[2]);
            }
            else if (cmd == "fs list")
            {
                if (!isFSloaded)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Filesystem not initialized!\nPlease initialize it with command \"fs load\"");
                    Console.ResetColor();
                    return;
                }
                Console.WriteLine($"Directories in 0:\\");
                foreach (string dir in System.IO.Directory.GetDirectories("0:\\"))
                {
                    Console.WriteLine($"- {dir}");
                }
                Console.WriteLine($"\nFiles in 0:\\");
                foreach (string file in System.IO.Directory.GetFiles("0:\\"))
                {
                    Console.WriteLine($"- {file}");
                }
            }
            else if (cmd == "")
            {
            }
            else if (isFSloaded && File.Exists($@"0:\System\bin\{comm[0]}.exe"))
            {
                List<string> Params = new List<string>();
                if (comm.Length > 1)
                {
                    for (int i = 1; i < comm.Length; i++)
                    {
                        Params.Add(comm[i]);
                    }
                }
                Executable exec = ConstructExecutable(File.ReadAllBytes($@"0:\System\bin\{comm[0]}.exe"), Params.ToArray());
                while (exec.running) {
                    exec.NextInstruction();
                }
                int code = 0;
                exec.Memory.Stack.Try_Pop(ref code);
                if (code != 0)
                    Console.WriteLine($"Program exited with abnormal code {code}");
            }
            else if (isFSloaded && cmd.Contains("&"))
            {
                string[] comms = cmd.Split('&');
                foreach (string _com in comms)
                {
                    string[] cm = _com.Split(' ');
                    List<string> Params = new List<string>();
                    if (comm.Length > 1)
                    {
                        for (int i = 1; i < comm.Length; i++)
                        {
                            Params.Add(cm[i]);
                        }
                    }
                    Sys.Emulation.Threading.Thread thread = new Sys.Emulation.Threading.Thread(ConstructExecutable(File.ReadAllBytes($@"0:\System\bin\{cm[0]}.exe")));
                    thread.Start();
                }
                while (true)
                    Sys.Emulation.Threading.TaskManager.Next();
            }
            else
            {
                Console.WriteLine("Command not found!");
            }
        }
        protected override void Run()
        {
            Console.Write($"{(services.Usermanager.CurrentUser ?? new services.Usermanager.User() { username = "unknown" }).username}@{Hostname}> ");
            string cmd = Console.ReadLine();
            RunCommand(cmd);
        }
    }
}
