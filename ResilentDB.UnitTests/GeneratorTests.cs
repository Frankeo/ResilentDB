using System.Threading;
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
        [InlineData(1000)]
        [InlineData(5000)]
        public void Generate_Different_Keys(int amountOfKeys)
        {
            ulong[] keys = new ulong[amountOfKeys];
            for (int i = 0; i < amountOfKeys; i++)
            {
                keys[i] = Generator.GetKey();
            }

            Assert.True(keys.All(new HashSet<ulong>().Add));
        }

        [Theory]
        [InlineData(2, 5000)]
        [InlineData(4, 5000)]
        [InlineData(8, 5000)]
        [InlineData(12, 5000)]
        [InlineData(16, 5000)]
        public void Thread_Safe_Different_Keys(int amountOfThreads, int amountOfKeys)
        {
            var treads = new Thread[amountOfThreads];
            ulong[][] keyResults = new ulong[amountOfThreads][];
            
            for (int i = 0; i < amountOfThreads; i++)
            {
                int j = i;
                treads[i] = new Thread(() => GenerateKeys(amountOfKeys, ref keyResults[j]));
            }

            foreach (var thread in treads)
            {
                thread.Start();                
            }        

            Thread.Sleep(1000);

            foreach (var thread in treads)
            {
                thread.Join();                
            }
                
            Assert.True(keyResults.All(result => result.All(new HashSet<ulong>().Add)));
            var list = new List<ulong>();
            foreach (var result in keyResults)
            {
                list.AddRange(result);   
            }            
            Assert.True(list.All(new HashSet<ulong>().Add));
        }

        private static void GenerateKeys(int amountOfKeys, ref ulong[] keyResult)
        {
            keyResult = new ulong[amountOfKeys];
            for (int i = 0; i < amountOfKeys; i++)
            {
                keyResult[i] = Generator.GetKey();
            }
        }
    }
}
