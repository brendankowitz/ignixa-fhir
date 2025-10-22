# Ignixa FHIR Server - License and Attribution

This project is composed of code from multiple sources, each with its own license and copyright.

## MIT Licensed Components

### Ignixa Contributors (Original Work)
**License**: MIT
**Copyright**: Copyright (c) Ignixa Contributors. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

### Microsoft FHIR Server - Architectural Inspiration
**Source**: https://github.com/microsoft/fhir-server
**License**: MIT
**Copyright**: Copyright (c) Microsoft Corporation. All rights reserved.

This project was inspired by and adapted from architectural patterns found in the
Microsoft FHIR Server, including design concepts for:
- CQRS handler patterns (Medino-based implementation)
- API endpoint and middleware architecture
- Bundle processing orchestration
- Multi-tenancy partition strategies
- Transaction coordination patterns
- Domain modeling and exception hierarchies

However, **the majority of implementation files have been substantially rewritten or
newly generated** for this project's specific needs. Many files retain Microsoft copyright
headers as a conservative attribution measure, but actual code lineage varies:

**Truly Derived** (minimal modifications from Microsoft FHIR Server):
- Core architectural abstractions (IFhirRepository, ISearchService)
- Exception base classes and standard FHIR exceptions
- Multi-tenancy partition strategy interfaces

**Substantially Modified/Rewritten** (Microsoft pattern + Ignixa implementation):
- CQRS handlers (adapted to Medino + custom business logic)
- Bundle processing (completely refactored for streaming + zero-copy)
- API endpoints (migrated from MVC Controllers → Minimal API)
- Metadata capability statement (rewritten using JsonNode)
- Search parameter handling (custom expression trees + indexing)

**Newly Generated/Custom** (no Microsoft FHIR Server basis):
- FHIRPath evaluation engine (custom Superpower-based parser)
- FHIR validation system (three-tier architecture)
- PATCH operations (FHIRPath expression support)
- Streaming bundle serialization (95% memory reduction)
- Multi-tenant data layer abstractions
- History operations (_history endpoints)
- Background job orchestration (DurableTask integration)
- Custom SourceNode serialization (zero-copy JSON)

Files retain Microsoft copyright headers for transparency, but understand that
many have been substantially rewritten. See CLAUDE.md section "Copyright Headers"
and individual file headers for more precise origin details.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## BSD 3-Clause Licensed Components

### Firely SDK - Derived Components
**Source**: https://github.com/FirelyTeam/firely-net-sdk
**License**: BSD 3-Clause License
**Copyright**: Copyright (c) 2015-2023, Firely (info@fire.ly) and contributors

Approximately 11 source files in this project are derived from the Firely .NET SDK,
including core abstractions:
- `Ignixa.SourceNodeSerialization/Abstractions/ISourceNode.cs`
- `Ignixa.SourceNodeSerialization/Abstractions/ITypedElement.cs`
- `Ignixa.SourceNodeSerialization/Abstractions/IAnnotated.cs`
- `Ignixa.SourceNodeSerialization/Abstractions/IBaseElementNavigator.cs`
- `Ignixa.SourceNodeSerialization/Abstractions/IElementDefinitionSummary.cs`
- `Ignixa.FhirPath/Expressions/Expression.cs`
- `Ignixa.FhirPath/Expressions/ISourcePositionInfo.cs`

These files retain their original Firely copyright headers and are licensed
under the BSD 3-Clause License:

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

---

## Identifying Source Code

Individual source files include copyright headers identifying their origin:

- **Microsoft FHIR Server files**: `// Copyright (c) Microsoft Corporation. All rights reserved.`
- **Firely SDK files**: `// Copyright (c) 2015-2023, Firely (info@fire.ly) and contributors`
- **Ignixa Contributors**: `// Copyright (c) Ignixa Contributors. All rights reserved.`

See `CLAUDE.md` section "Copyright Headers" for the complete copyright policy
and file classification.
