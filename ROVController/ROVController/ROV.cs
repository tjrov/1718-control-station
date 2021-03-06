﻿using System;
using System.Timers;
using System.IO.Ports;
using Modbus.Device;
using System.Diagnostics;

namespace ROVController
{
    public class ROV
    {
        private bool isConnected;
        private bool communicationWatchdog;
        private SerialPort serialPort;
        private IModbusSerialMaster modbus;
        private ushort[] registers;

        private Timer timer;
        private int loopCounter;
        public Thruster[] Thrusters;
        public Avionics Avionics;
        public ROVOutput[] Manipulators;
        public ROVInput Voltmeter;
        public ROVOutput Relays;

        public ROV(string portName, int baudRate, int updateInterval)
        {
            //connect to ROV computer
            try
            {
                serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                serialPort.Open();
                modbus = ModbusSerialMaster.CreateRtu(serialPort);
                isConnected = true;
            } catch(Exception e)
            {
                throw new Exception("Failed to start communication: " + e.Message);
            }

            registers = new ushort[29];
            for (int i = 0; i < 29; i++)
            {
                //set all to max value to indicate not operational
                registers[i] = 0;
            }

            //set up thrusters, avionics, manipulator objects with
            //specific indices in register array
            Thrusters = new Thruster[6];
            for (int i = 0; i < 6; i++)
            {
                Thrusters[i] = new Thruster(registers, i, 16 + i, 22 + i);
            }

            Avionics = new Avionics(registers, 6, 7, 8, 9);

            Manipulators = new ROVOutput[4];
            for (int i = 0; i < 4; i++)
            {
                Manipulators[i] = new ROVOutput(registers, 10 + i, -255, 255);
            }

            Relays = new ROVOutput(registers, 14, UInt16.MinValue, UInt16.MaxValue);

            Voltmeter = new ROVInput(registers, 15, 0, 30);

            //every x milliseconds send/receive data with ROV
            timer = new Timer(updateInterval);
            timer.AutoReset = true;
            timer.Elapsed += CommunicationTimerElapsed;
            timer.Start();
            loopCounter = 0;
        }

        public bool IsConnected { get { return isConnected; } }

        public void Reconnect()
        {
            if(!serialPort.IsOpen)
            {
                try
                {
                    serialPort.Open();
                    isConnected = true;
                    communicationWatchdog = false;
                } catch { }
            }
        }

        //runs 100Hz
        private void CommunicationTimerElapsed(object o, ElapsedEventArgs ea)
        {
            if (serialPort.IsOpen)
            {
                if(communicationWatchdog)
                {
                    isConnected = false;
                    for(int i = 0; i < registers.Length; i++)
                    {
                        //set registers so that all instruments show failure
                        registers[i] = 0;
                    }
                }
                communicationWatchdog = true;
                loopCounter++;
                if (loopCounter >= 10)
                {
                    LowPriorityCommunication();
                    loopCounter = 0;
                }
                HighPriorityCommunication();
                communicationWatchdog = false;
            } else
            {
                Reconnect();
            }
        }

        //also runs 100Hz
        private void HighPriorityCommunication()
        {
            //send thruster values
            WriteRegisters(0, 6);
            //get avionics data
            ReadRegisters(6, 4);
        }

        //runs 10Hz
        private void LowPriorityCommunication()
        {
            //send booleans for relays, lights, manipulators
            WriteRegisters(10, 5);
            //read thruster rpm, temp, ROV status and error codes
            ReadRegisters(15, 14);
        }

        //takes subset of surface register array and writes it to ROV register array
        private void WriteRegisters(int start, int n)
        {
            ushort[] subset = new ushort[n];
            for (int i = 0; i < n; i++)
            {
                subset[i] = registers[start + i];
            }
            if (serialPort.IsOpen && isConnected)
            {
                try
                {
                    modbus.WriteMultipleRegisters(1, (ushort)start, subset);
                }
                catch { isConnected = false; }
            }
        }

        //reads subset of ROV's register array and adds it to surface register array
        private void ReadRegisters(int start, int n)
        {
            if (serialPort.IsOpen && isConnected) {
                try
                {
                    ushort[] subset = modbus.ReadHoldingRegisters(1, (ushort)start, (ushort)n);
                    for (int i = 0; i < n; i++)
                    {
                        registers[start + i] = subset[i];
                    }
                }
                catch { isConnected = false; }
            }
        }

        private byte GetLastCommunicationError()
        {
            return (byte)(registers[28] >> 8);
        }

        public string GetLastCommunicationErrorString()
        {
            /*From ModbusRtu.h, getLastError() returns the following values:
            ERR_NOT_MASTER = -1,
            ERR_POLLING = -2,
            ERR_BUFF_OVERFLOW = -3,
            ERR_BAD_CRC = -4,
            ERR_EXCEPTION = -5*/
            switch ((int)GetLastCommunicationError())
            {
                case 0:
                    return "0: No Error";
                case -1:
                    return "-1: You are not a master device";
                case -2:
                    return "-2: Error polling slave";
                case -3:
                    return "-3: Slave received data buffer overflow";
                case -4:
                    return "-4: Bad checksum on message";
                case -5:
                    return "-5: Communication exception";
                default:
                    return GetLastCommunicationError() + ": Unknown error";
            }
        }

        public byte GetCommunicationErrorCount()
        {
            return (byte)registers[28];
        }
    }
    public class Thruster
    {
        public ROVOutput Speed;
        public ROVInput Tachometer, Thermometer;
        public Thruster(ushort[] registers, int speedAddr, int rpmAddr, int tempAddr)
        {
            Speed = new ROVOutput(registers, speedAddr, Int16.MinValue, Int16.MaxValue);
            Tachometer = new ROVInput(registers, rpmAddr, UInt16.MinValue, UInt16.MaxValue);
            Thermometer = new ROVInput(registers, tempAddr, 0, 100);
        }
        public bool OverRPM { get { return Tachometer.Value > 1000; } } //over 1000 RPM
        public bool OverTemp { get { return Thermometer.Value > 50; } } //over 50 degC or 122 degF
        public bool IsAlive { get { return Thermometer.IsAlive && Tachometer.IsAlive; } }
    }
    public class Avionics
    {
        public ROVInput Yaw, Pitch, Roll, Depth;
        public Avionics(ushort[] registers, int yawAddr, int pitchAddr, int rollAddr, int depthAddr)
        {
            Yaw = new ROVInput(registers, yawAddr, -Math.PI, Math.PI);
            Pitch = new ROVInput(registers, pitchAddr, -Math.PI, Math.PI);
            Roll = new ROVInput(registers, rollAddr, -Math.PI, Math.PI);
            Depth = new ROVInput(registers, depthAddr, 0, 25);
        }
        public bool IMUAlive { get { return Yaw.IsAlive && Pitch.IsAlive && Roll.IsAlive; } }
    }
    public class ROVOutput
    {
        private double outputValue;
        private LinearMapping mapping;
        private int registerIndex;
        private ushort[] registers;
        public ROVOutput(ushort[] registers, int registerIndex, double fromMin, double fromMax)
        {
            mapping = new LinearMapping(fromMin, fromMax, UInt16.MinValue, UInt16.MaxValue);
            this.registerIndex = registerIndex;
            this.registers = registers;
        }
        public double Value
        {
            get
            {
                return outputValue;
            }
            set
            {
                if (mapping.InRange(value))
                {
                    outputValue = value;
                    registers[registerIndex] = (ushort)mapping.Map(value);
                }
                else
                {
                    throw new Exception("Value is out of range");
                }
            }
        }
    }
    public class ROVInput
    {
        private LinearMapping mapping;
        private int registerIndex;
        private ushort[] registers;
        public ROVInput(ushort[] registers, int registerIndex, double toMin, double toMax)
        {
            mapping = new LinearMapping(UInt16.MinValue, UInt16.MaxValue, toMin, toMax);
            this.registerIndex = registerIndex;
            this.registers = registers;
        }
        public double Value
        {
            get
            {
                return mapping.Map(registers[registerIndex]);
            }
        }
        public bool IsAlive
        {
            get
            {
                return registers[registerIndex] != 0;
            }
        }
    }
    public class LinearMapping
    {
        private double fromMin, fromMax, toMin, toMax;
        public LinearMapping(double fromMin, double fromMax, double toMin, double toMax)
        {
            this.fromMin = fromMin;
            this.fromMax = fromMax;
            this.toMin = toMin;
            this.toMax = toMax;
        }
        public double Map(double from)
        {
            return (from - fromMin) / (fromMax - fromMin) * (toMax - toMin) + toMin;
        }
        public bool InRange(double from)
        {
            return from >= fromMin && from <= fromMax;
        }
    }
}
