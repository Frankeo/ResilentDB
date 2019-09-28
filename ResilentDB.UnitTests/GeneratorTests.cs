using System.Security.AccessControl;
using System;
using System.Collections.Generic;
using Xunit;
using KeyGenerator;
using System.Linq;

namespace ResilentDB.UnitTests
{
    public class GeneratorTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Generate_Different_Keys(int amountOfKeys)
        {
            ulong[] keys = new ulong[amountOfKeys];
            for (int i = 0; i < amountOfKeys; i++)
            {
                keys[i] = Generator.GetKey();
            }

            var diffChecker = new HashSet<ulong>();            
            Assert.True(keys.All(diffChecker.Add));            
        }
    }
}
