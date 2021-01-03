using sys = System;
using System.Collections;
using System.Collections.Generic;
using Cosmos.Core;
using Cosmos.System;

namespace Cosmos.System.PS2Support.Devices
{
    public class PS2Mouse
    {
        byte offset = 0;
        byte buttons = 0;
        List<byte> buffer = new List<byte>(3);

        IOPort commandPort = new IOPort(0x64), dataPort = new IOPort(0x60);
        public void Enable()
        {
            MouseManager.X = 0;
            MouseManager.Y = 0;
            INTs.SetIrqHandler(0x0C, HandleInterrupt);

            commandPort.Byte = 0xA8;
            commandPort.Byte = 0x20;
            byte status = (byte)(dataPort.Byte | 2);
            commandPort.Byte = 0x60;
            dataPort.Byte = status;

            commandPort.Byte = 0xD4;
            dataPort.Byte = 0xF4;
            _ = dataPort.Byte;
            for (int i = 0; i < 3; i++)
            {
                buffer.Add(0x00);
            }
        }
        private void HandleInterrupt(ref INTs.IRQContext context)
        {
            try
            {
                byte status = commandPort.Byte;
                if ((byte)(status & 20) == 0) return;
                buffer[offset] = dataPort.Byte;
                offset = (byte)((offset + 1) % 3);
                if (offset == 0)
                {
                    for (byte i = 0; i < 3; i++)
                    {
                        if ((buffer[0] & (0x1 << i)) != (buttons & (0x1 << i)))
                        {
                            if ((byte)(buttons & (0x1 << i)) != 0)
                                MouseManager.MouseState = MouseState.None;
                            else
                                MouseManager.MouseState = MouseState.Left;
                        }
                    }
                    if (buffer[1] != 0 || buffer[2] != 0)
                    {
                        MouseManager.HandleMouse((sbyte)buffer[1], (sbyte)-buffer[2], (int)MouseManager.MouseState, 0);
                    }
                    buttons = buffer[0];
                }
            }
            catch (sys.Exception ex)
            {
            }
        }
        public void Disable()
        {
        }
    }
}
namespace Cosmos.System.PS2Support
{
    public static class Global
    {
        public static void InitPS2Port()
        {
            IOPort commandPort = new IOPort(0x64), dataPort = new IOPort(0x60);
            commandPort.Byte = 0xFF;
            sys.Console.WriteLine("PS2 ports enabled correctly");
        }
    }
}
