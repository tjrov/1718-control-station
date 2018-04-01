// DllTest.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "C:\rov\1718-control-station\GUI_3_29\main.cpp"
#include "C:\rov\1718-control-station\GUI_3_29\SerialPort.cpp"
#include "C:\rov\1718-control-station\GUI_3_29\SerialPort.h"
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

extern "C" __declspec(dllexport) SerialPort::SerialPort(char *portname)
{
	SerialPort serialport(portname);
}

extern "C" __declspec(dllexport) void Connect(char *newCom)
{
	robot = new SerialPort(port_name);
	if (robot->isConnected())
		cout << "Connection Established" << endl;
	else
		cout << "ERROR, check port name" << endl;
}

extern "C" __declspec(dllexport) void ComLoop()
{
	if (robot->isConnected())
	{
		int read_result = robot->readSerialPort(incomingData, MAX_DATA_LENGTH);
		//prints out data
		cout << incomingData << endl;
	}
	else
		cout << "Not Connected" << endl;
	return;
}

extern "C" __declspec(dllexport) int SerialPort::readSerialPort(char *buffer, unsigned int buf_size)
{
	DWORD bytesRead;
	unsigned int toRead;

	ClearCommError(this->handler, &this->errors, &this->status);

	if (this->status.cbInQue > 0) {
		if (this->status.cbInQue > buf_size) {
			toRead = buf_size;
		}
		else toRead = this->status.cbInQue;
	}

	if (ReadFile(this->handler, buffer, toRead, &bytesRead, NULL)) return bytesRead;

	return 0;
}
extern "C" __declspec(dllexport) bool SerialPort::writeSerialPort(char *buffer, unsigned int buf_size)
{
	DWORD bytesSend;

	if (!WriteFile(this->handler, (void*)buffer, buf_size, &bytesSend, 0)) {
		ClearCommError(this->handler, &this->errors, &this->status);
		return false;
	}
	else return true;
}
extern "C" __declspec(dllexport) bool SerialPort::isConnected()
{
	return this->connected;
}
