//
// XUnitProjectTestSuite.cs
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
using System.Linq;
using MonoDevelop.UnitTesting;
using MonoDevelop.Projects;
using MonoDevelop.Ide;
using System.Collections.Generic;
using System.IO;
using MonoDevelop.Ide.TypeSystem;
using System.Threading.Tasks;
using System.Threading;
using Mono.Addins;

namespace MonoDevelop.XUnit
{

    public abstract class XUnitSourceCodeLocationFinder
    {
        static List<XUnitSourceCodeLocationFinder> locationFinder = new List<XUnitSourceCodeLocationFinder> ();

        static XUnitSourceCodeLocationFinder ()
        {
            AddinManager.AddExtensionNodeHandler ("/MonoDevelop/UnitTesting/XUnitSourceCodeLocationFinder", delegate (object sender, ExtensionNodeEventArgs args) {
                var provider = (XUnitSourceCodeLocationFinder)args.ExtensionObject;
                switch (args.Change) {
                case ExtensionChange.Add:
                    locationFinder.Add (provider);
                    break;
                case ExtensionChange.Remove:
                    locationFinder.Remove (provider);
                    break;
                }
            });
        }

        public static async Task<SourceCodeLocation> TryGetSourceCodeLocationAsync (Project project, string fixtureTypeNamespace, string fixtureTypeName, string testName, CancellationToken cancellationToken = default (CancellationToken))
        {
            foreach (var finder in locationFinder) {
                var result = await finder.GetSourceCodeLocationAsync (project, fixtureTypeNamespace, fixtureTypeName, testName, cancellationToken).ConfigureAwait (false);
                if (result != null)
                    return result;
            }
            return null;
        }

        public abstract Task<SourceCodeLocation> GetSourceCodeLocationAsync (Project project, string fixtureTypeNamespace, string fixtureTypeName, string testName, CancellationToken cancellationToken = default (CancellationToken));
    }
}
