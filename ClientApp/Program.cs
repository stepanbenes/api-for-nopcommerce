using System;

namespace ClientApp
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Types in this assembly:");
			foreach (Type t in typeof(Program).Assembly.GetTypes())
			{
				Console.WriteLine(t.FullName);
			}
			Console.ReadKey();
		}
	}
}
