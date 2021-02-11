using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TTransfer.Network
{
    class CommunicationResult
    {
        // Public
        public TTInstruction Instruction { get { return instruction; } }
        public byte[] Data { get { return data; } }
        public bool Success { get { return success; } }


        // Data
        TTInstruction instruction;
        byte[] data;
        bool success;



        public CommunicationResult()
        {
            instruction = TTInstruction.Empty;
            data = null;
            success = false;
        }
        public CommunicationResult(TTInstruction instruction, byte[] data)
        {
            this.instruction = instruction;
            this.data = data;
            success = true;
        }
    }
}
