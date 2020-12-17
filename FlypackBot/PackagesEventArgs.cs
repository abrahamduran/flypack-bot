using System;
using System.Collections.Generic;
using FlypackBot.Models;

namespace FlypackBot
{
    public class PackagesEventArgs : EventArgs
    {
        public IEnumerable<Package> Packages { get; private set; }
        public Dictionary<string, Package> PreviousPackages { get; private set; }

        public PackagesEventArgs(IEnumerable<Package> packages, Dictionary<string, Package> previousPackages)
        {
            Packages = packages;
            PreviousPackages = previousPackages;
        }
    }
}
