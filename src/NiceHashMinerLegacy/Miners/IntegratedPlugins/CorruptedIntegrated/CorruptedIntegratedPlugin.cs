using CorruptedPlugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NiceHashMiner.Miners.IntegratedPlugins
{ 
    class CorruptedIntegratedPlugin : CorruptedPlugin.CorruptedPlugin, IntegratedPlugin
    {
        public CorruptedIntegratedPlugin() : base("Corrupted")
        { }

        public bool Is3rdParty => true;
    }
}
