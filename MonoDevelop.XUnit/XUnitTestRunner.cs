﻿//
// XUnitTestRunner.cs
//
// Author:
//       Sergey Khabibullin <sergey@khabibullin.com>
//
// Copyright (c) 2014 Sergey Khabibullin
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using MonoDevelop.Core.Execution;
using System.Linq;
using System.IO;
using System.Threading;

namespace MonoDevelop.XUnit
{
	/// <summary>
	/// Wrapper around xUnit.net framework internals. Extracts and runs tests.
	/// </summary>
	public class XUnitTestRunner: RemoteProcessObject
	{
		public XUnitTestInfo GetTestInfo (string assembly, string[] supportAssemblies)
		{
			PreloadAssemblies (supportAssemblies);
			return BuildTestInfo (assembly);
		}

		void PreloadAssemblies (params string[] supportAssemblies)
		{
			foreach (string assembly in supportAssemblies)
				Assembly.LoadFrom (assembly);
		}

		XUnitTestInfo BuildTestInfo (string assembly)
		{
			var infos = new List<TestCaseInfo> ();
            // TODO: use TestDiscoveryVisitor to locate test cases.
			if (assembly != null && File.Exists (assembly)) {
				using (var controller = new XunitFrontController (assembly, null, false))
				using (var discoveryVisitor = new TestDiscoveryVisitor ()) {
					controller.Find (false, discoveryVisitor, TestFrameworkOptions.ForDiscovery ());
					discoveryVisitor.Finished.WaitOne ();

					foreach (var testCase in discoveryVisitor.TestCases) {
						infos.Add (new TestCaseInfo {
							Id = testCase.UniqueID,
							Type = testCase.TestMethod.TestClass.Class.Name,
							Method = testCase.TestMethod.Method.Name,
							DisplayName = testCase.DisplayName
						});
					}
				}

				// sort by type, method
				infos.Sort ((info1, info2) => {
					int i = info1.Type.CompareTo (info2.Type);
					if (i == 0)
						i = info1.Method.CompareTo (info2.Method);
					return i;
				});
			}

			var testInfo = new XUnitTestInfo ();
			BuildTestInfo (testInfo, infos, 0);
			return testInfo;
		}

		void BuildTestInfo (XUnitTestInfo testInfo, IEnumerable<TestCaseInfo> infos, int step)
		{
			int count = infos.Count ();

			if (count == 0)
				return;

			var firstItem = infos.First ();

			// if the test is the last element in the group
			// then it's going to be a leaf node in the structure
			if (count == 1) {
				if (step == firstItem.NameParts.Length) {
					testInfo.Id = firstItem.Id;
					testInfo.Type = firstItem.Type;
					testInfo.Method = firstItem.Method;
					testInfo.Name = firstItem.Name;
					return;
				}
			}

			// build the tree structure based on the parts of the name, so
			// [a.b.c, a.b.d, a.e] would become
			//  (a)
			//   |-(b)
			//      |-(c)
			//      |-(d)
			//   |-(e)
			var groups = infos.GroupBy (info => info.NameParts [step]);
			var children = new List<XUnitTestInfo> ();
			foreach (var group in groups) {
				var child = new XUnitTestInfo {
					Name = group.Key
				};

				BuildTestInfo (child, group, step + 1);
				children.Add (child);
			}
			testInfo.Tests = children.ToArray ();
		}

		public void Execute (string assembly, XUnitTestInfo[] testInfos, IXUnitExecutionListener executionListener)
		{
			var lookup = new HashSet<string> ();
			foreach (var testInfo in testInfos)
				lookup.Add (testInfo.Id);

			// we don't want to run every test in the assembly
			// only the tests passed in "testInfos" argument
			using (var controller = new XunitFrontController (assembly, null, false))
			using (var discoveryVisitor = new TestDiscoveryVisitor (tc => lookup.Contains (tc.UniqueID)))
			using (var executionVisitor = new TestExecutionVisitor (executionListener)) {
				controller.Find(false, discoveryVisitor, TestFrameworkOptions.ForDiscovery ());
				discoveryVisitor.Finished.WaitOne ();

                var options = TestFrameworkOptions.ForExecution (LoadConfiguration(assembly));
				controller.RunTests (discoveryVisitor.TestCases, executionVisitor, options);
			}
		}

        static Stream GetConfigurationStreamForAssembly(string assemblyName)
        {
            // See if there's a directory with the assm name. this might be the case for appx
            if (Directory.Exists(assemblyName))
            {
                if (File.Exists(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json"));

                if (File.Exists(Path.Combine(assemblyName, "xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, "xunit.runner.json"));
            }

            // Fallback to working dir
            if (File.Exists($"{assemblyName}.xunit.runner.json"))
                return File.OpenRead($"{assemblyName}.xunit.runner.json");

            if (File.Exists("xunit.runner.json"))
                return File.OpenRead("xunit.runner.json");

            return null;
        }

        static TestAssemblyConfiguration LoadConfiguration(string assemblyName)
        {
            #if PLATFORM_DOTNET
            var stream = GetConfigurationStreamForAssembly(assemblyName);
            return stream == null ? new TestAssemblyConfiguration() : ConfigReader.Load(stream);
            #else
            return ConfigReader.Load(assemblyName);
            #endif
        }

		class TestCaseInfo
		{
			public string Id;
			public string Type;
			public string Method;
			public string DisplayName;

			string name;
			public string Name {
				get {
					if (name == null) {
						if (DisplayName.StartsWith (Type + "." + Method))
							name = Method;
						else
							name = DisplayName;
					}
					return name;
				}
			}

			void parseName ()
			{
				// TODO: fix for xunit v2 where each theory is a separate test case
				string[] typeParts = Type.Split ('.');
				nameParts = new string [typeParts.Length + 1];
				typeParts.CopyTo (nameParts, 0);
				nameParts [typeParts.Length] = Method;
			}

			string[] nameParts;
			public string[] NameParts {
				get {
					if (nameParts == null)
						parseName ();
					return nameParts;
				}
			}
		}
	}

	public interface IXUnitExecutionListener
	{
		bool IsCancelRequested { get; }
		void OnTestCaseStarting (string id);
		void OnTestCaseFinished (string id);
		void OnTestFailed (string id,
			decimal executionTime, string output, string[] exceptionTypes, string[] messages, string[] stackTraces);
		void OnTestPassed (string id,
			decimal executionTime, string output);
		void OnTestSkipped (string id,
			string reason);
	}

	public class TestDiscoveryVisitor: TestMessageVisitor<IDiscoveryCompleteMessage>
	{
		public List<ITestCase> TestCases { get; private set; }
		Func<ITestCase, bool> filter;

		public TestDiscoveryVisitor ()
		{
			TestCases = new List<ITestCase> ();
		}

		public TestDiscoveryVisitor (Func<ITestCase, bool> filter): this ()
		{
			this.filter = filter;
		}

		protected override bool Visit (ITestCaseDiscoveryMessage discovery)
		{
			if (filter == null || filter (discovery.TestCase))
				TestCases.Add (discovery.TestCase);

			return true;
		}
	}

	public class TestExecutionVisitor: TestMessageVisitor<ITestAssemblyFinished>
	{
		IXUnitExecutionListener executionListener;

		public TestExecutionVisitor (IXUnitExecutionListener executionListener)
		{
			this.executionListener = executionListener;
		}

		protected override bool Visit (ITestCaseStarting testCaseStarting)
		{
			executionListener.OnTestCaseStarting (testCaseStarting.TestCase.UniqueID);
			return !executionListener.IsCancelRequested;
		}

		protected override bool Visit (ITestCaseFinished testCaseFinished)
		{
			executionListener.OnTestCaseFinished (testCaseFinished.TestCase.UniqueID);
			return !executionListener.IsCancelRequested;
		}

		protected override bool Visit (ITestFailed testFailed)
		{
			executionListener.OnTestFailed (testFailed.TestCase.UniqueID,
				testFailed.ExecutionTime, testFailed.Output, testFailed.ExceptionTypes, testFailed.Messages, testFailed.StackTraces);
			return !executionListener.IsCancelRequested;
		}

		protected override bool Visit (ITestPassed testPassed)
		{
			executionListener.OnTestPassed (testPassed.TestCase.UniqueID, testPassed.ExecutionTime, testPassed.Output);
			return !executionListener.IsCancelRequested;
		}

		protected override bool Visit (ITestSkipped testSkipped)
		{
			executionListener.OnTestSkipped (testSkipped.TestCase.UniqueID, testSkipped.Reason);
			return !executionListener.IsCancelRequested;
		}
	}

}

