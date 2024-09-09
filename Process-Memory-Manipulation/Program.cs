using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    // Estrutura para capturar informações dos processos
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    };

    // Constantes usadas nas chamadas API
    const uint TH32CS_SNAPPROCESS = 0x00000002;
    const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
    const int MAX_PATH = 260;

    // DllImports para interagir com o sistema
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool EnumProcessModules(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, [MarshalAs(UnmanagedType.U4)] out uint lpcbNeeded);

    [DllImport("psapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, [In][MarshalAs(UnmanagedType.U4)] int nSize);

    // Função para log de mensagens
    static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    // Função para encontrar o ID do processo
    static uint FindProcessId(string processName)
    {
        try
        {
            PROCESSENTRY32 pe32 = new PROCESSENTRY32();
            pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

            IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hSnapshot == IntPtr.Zero)
            {
                Log("Erro ao criar snapshot do processo.");
                return 0;
            }

            if (Process32First(hSnapshot, ref pe32))
            {
                do
                {
                    if (pe32.szExeFile.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    {
                        CloseHandle(hSnapshot);
                        Log($"Processo {processName} encontrado. ID: {pe32.th32ProcessID}");
                        return pe32.th32ProcessID;
                    }
                } while (Process32Next(hSnapshot, ref pe32));
            }

            CloseHandle(hSnapshot);
            Log($"Processo {processName} não encontrado.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"Erro ao encontrar o ID do processo: {ex.Message}");
            return 0;
        }
    }

    // Função para obter o endereço base do módulo
    static IntPtr GetModuleBaseAddress(IntPtr hProcess, string moduleName)
    {
        try
        {
            IntPtr[] hModules = new IntPtr[1024];
            uint cb = (uint)(IntPtr.Size * hModules.Length);
            uint cbNeeded;

            if (EnumProcessModules(hProcess, hModules, cb, out cbNeeded))
            {
                for (int i = 0; i < (cbNeeded / IntPtr.Size); i++)
                {
                    StringBuilder moduleBaseName = new StringBuilder(MAX_PATH);
                    GetModuleBaseName(hProcess, hModules[i], moduleBaseName, moduleBaseName.Capacity);

                    if (moduleBaseName.ToString().Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Módulo {moduleName} encontrado. Endereço Base: {hModules[i].ToString("X")}");
                        return hModules[i];
                    }
                }
            }

            Log($"Módulo {moduleName} não encontrado.");
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Log($"Erro ao obter o endereço base do módulo: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    // Função para encontrar o ponteiro dinâmico
    static IntPtr FindDynamicPointer(IntPtr hProcess, IntPtr baseAddress, int[] offsets)
    {
        try
        {
            IntPtr pointer = baseAddress;
            byte[] buffer = new byte[IntPtr.Size];
            IntPtr bytesRead;

            foreach (int offset in offsets)
            {
                if (!ReadProcessMemory(hProcess, pointer, buffer, (uint)buffer.Length, out bytesRead) || bytesRead.ToInt32() != buffer.Length)
                {
                    Log($"Erro ao ler memória no endereço: {pointer.ToString("X")}");
                    return IntPtr.Zero;
                }

                pointer = (IntPtr.Size == 4) ? (IntPtr)BitConverter.ToInt32(buffer, 0) : (IntPtr)BitConverter.ToInt64(buffer, 0);
                pointer = IntPtr.Add(pointer, offset);
                Log($"Offset: {offset.ToString("X")}, Endereço Atual: {pointer.ToString("X")}");
            }

            Log($"Ponteiro dinâmico encontrado: {pointer.ToString("X")}");
            return pointer;
        }
        catch (Exception ex)
        {
            Log($"Erro ao encontrar o ponteiro dinâmico: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    // Função para alterar valor na memória (int)
    static void ChangeValueOnMemory(IntPtr hProcess, IntPtr moduleBaseAddress, Int32 address, int[] offsets, int newValue, string choiceName)
    {
        IntPtr baseAddress = IntPtr.Add(moduleBaseAddress, address);
        IntPtr targetAddress = FindDynamicPointer(hProcess, baseAddress, offsets);

        if (targetAddress == IntPtr.Zero)
        {
            return;
        }

        byte[] buffer = BitConverter.GetBytes(newValue);
        if (WriteProcessMemory(hProcess, targetAddress, buffer, (uint)buffer.Length, out IntPtr bytesWritten))
        {
            Log($"Valor do(a) {choiceName} alterado com sucesso!");
        }
        else
        {
            Log($"Erro ao alterar o valor do(a) {choiceName}");
        }
    }

    // Função para alterar valor na memória (float)
    static void ChangeValueOnMemory(IntPtr hProcess, IntPtr moduleBaseAddress, Int32 address, int[] offsets, float newValue, string choiceName)
    {
        IntPtr baseAddress = IntPtr.Add(moduleBaseAddress, address);
        IntPtr targetAddress = FindDynamicPointer(hProcess, baseAddress, offsets);

        if (targetAddress == IntPtr.Zero)
        {
            return;
        }

        byte[] buffer = BitConverter.GetBytes(newValue);
        if (WriteProcessMemory(hProcess, targetAddress, buffer, (uint)buffer.Length, out IntPtr bytesWritten))
        {
            Log($"Valor do(a) {choiceName} alterado com sucesso!");
        }
        else
        {
            Log($"Erro ao alterar o valor do(a) {choiceName}");
        }
    }

    static void Main()
    {
        string processName = "Survivor.exe";
        string moduleName = "UnityPlayer.dll";
        string choiceName;
        uint processID = FindProcessId(processName);
        if (processID == 0)
        {
            Log("Finalizando o programa devido à falha na busca do processo.");
            return;
        }

        IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processID);
        if (hProcess == IntPtr.Zero)
        {
            Log("Erro ao abrir o processo.");
            return;
        }

        IntPtr moduleBaseAddress = GetModuleBaseAddress(hProcess, moduleName);
        if (moduleBaseAddress == IntPtr.Zero)
        {
            CloseHandle(hProcess);
            return;
        }

        while (true)
        {
            Console.WriteLine("Selecione a opção:");
            Console.WriteLine("1. Alterar contador de kills");
            Console.WriteLine("2. Alterar moedas");
            Console.WriteLine("3. Alterar Dano");
            Console.WriteLine("4. Alterar vida");
            Console.WriteLine("5. Alterar velocidade");
            Console.WriteLine("6. Sair");
            string choice = Console.ReadLine();
            Console.WriteLine("Digite o valor desejado");
            string valueInput = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    choiceName = "Contador de kills";
                    if (int.TryParse(valueInput, out int newKillsValue))
                    {
                        ChangeValueOnMemory(hProcess, moduleBaseAddress, 0x01AC7020, new int[] { 0x2F0, 0x30, 0x18, 0x28, 0x110 }, newKillsValue, choiceName);
                    }
                    else
                    {
                        Log("Valor inválido. Por favor, insira um número inteiro.");
                    }
                    break;
                case "2":
                    choiceName = "Moedas";
                    if (int.TryParse(valueInput, out int newCoinsValue))
                    {
                        ChangeValueOnMemory(hProcess, moduleBaseAddress, 0x01AC7308, new int[] { 0x0, 0x578, 0xA8, 0x28, 0x108 }, newCoinsValue, choiceName);
                    }
                    else
                    {
                        Log("Valor inválido. Por favor, insira um número inteiro.");
                    }
                    break;

                case "3":
                    choiceName = "Dano";
                    if (float.TryParse(valueInput, out float newDamageValue))
                    {
                        ChangeValueOnMemory(hProcess, moduleBaseAddress, 0x01ACAC98, new int[] { 0xB0, 0x68, 0xD8, 0x28, 0x60 }, newDamageValue, choiceName);
                    }
                    else
                    {
                        Log("Valor inválido. Por favor, insira um número decima.");
                    }
                    break;

                case "4":
                    choiceName = "Vida";
                    if (float.TryParse(valueInput, out float newLifeValue))
                    {
                        ChangeValueOnMemory(hProcess, moduleBaseAddress, 0x01ACAC98, new int[] { 0xB0, 0x68, 0xC0, 0x28, 0x50 }, newLifeValue, choiceName);
                    }
                    else
                    {
                        Log("Valor inválido. Por favor, insira um número decimal.");
                    }
                    break;
                case "5":
                    choiceName = "Speed";
                    if (float.TryParse(valueInput, out float newSpeedValue))
                    {
                        ChangeValueOnMemory(hProcess, moduleBaseAddress, 0x01ACAC98, new int[] { 0xB0, 0x68, 0x110, 0x28, 0x54 }, newSpeedValue, choiceName);
                    }
                    else
                    {
                        Log("Valor inválido. Por favor, insira um número decimal.");
                    }
                    break;
                case "6":
                    CloseHandle(hProcess);
                    return;
                default:
                    Console.WriteLine("Opção inválida. Tente novamente.");
                    break;
            }
        }
    }
}