using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using Xunit;
using AutoFixture.Xunit2;
using AutoFixture;
using KeyGenerator;
using System.Linq;

namespace ResilentDB.UnitTests
{
    public class GeneratorTests
    {
        readonly IFixture Fixture;
        public GeneratorTests()
        {
            Fixture = new Fixture();
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Generate_Different_Keys(int amountOfKeys)
        {
            ulong[] keys = new ulong[amountOfKeys];
            for (int i = 0; i < amountOfKeys; i++)
            {
                keys[i] = Generator.GetInstance().GetKey();
            }

            Assert.True(keys.All(new HashSet<ulong>().Add));
        }

        [Theory]
        [InlineData(2, 5000)]
        [InlineData(4, 5000)]
        [InlineData(8, 5000)]
        [InlineData(12, 5000)]
        [InlineData(16, 5000)]
        [InlineData(20, 5000)]
        public void Thread_Safe_Different_Keys(int amountOfThreads, int amountOfKeys)
        {
            //Arrange            
            ulong[][] keyResults = new ulong[amountOfThreads][];
            Parallel.For(0, amountOfThreads, i => GenerateKeys(amountOfKeys, ref keyResults[i]));

            //Assert                
            //Every element on every list are distintct inside the same list.
            Assert.True(keyResults.All(result => result.Distinct().Count() == result.Count()));

            //Every element is distinct on a list containing all the elements.
            Assert.True(keyResults.SelectMany(result => result).All(new HashSet<ulong>().Add));
        }

        private void ExecuteThreads(Thread[] threads)
        {
            foreach (var thread in threads)
                thread.Start();

            Thread.Sleep(1000);

            foreach (var thread in threads)
                thread.Join();
        }

        private Thread[] InitilizeThreads(int amountOfThreads, int amountOfKeys, ulong[][] keyResults)
        {
            var threads = new Thread[amountOfThreads];
            for (int i = 0; i < amountOfThreads; i++)
            {
                int j = i;
                threads[i] = new Thread(() => GenerateKeys(amountOfKeys, ref keyResults[j]));
            }
            return threads;
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
