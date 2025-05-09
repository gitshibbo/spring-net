/*
 * Copyright � 2002-2011 the original author or authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using NUnit.Framework;

namespace Spring.Objects.Factory.Config;

/// <summary>
/// Unit tests for the SpecialFolderVariableSource class.
/// </summary>
/// <author>Aleksandar Seovic</author>
[TestFixture]
public sealed class SpecialFolderVariableSourceTests
{
    [Test]
    public void TestVariablesResolution()
    {
        SpecialFolderVariableSource vs = new SpecialFolderVariableSource();

        // existing vars
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            vs.ResolveVariable("ApplicationData"));
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            vs.ResolveVariable("desktopDirectory"));
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            vs.ResolveVariable("programFiles"));
        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            vs.ResolveVariable("personal"));

        // non-existant variable
        Assert.IsNull(vs.ResolveVariable("dummy"));
    }
}