## .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3 (Job: ShortRun(IterationCount=10, LaunchCount=1, WarmupCount=5))

```assembly
; Ignixa.Benchmarks.FhirPathILBenchmarks.IgnixaSimplePath()
       push      rbx
       sub       rsp,20
       mov       rcx,[rcx+10]
       mov       rdx,227802C9E38
       xor       r8d,r8d
       call      qword ptr [7FF8B0074138]; Ignixa.FhirPath.Evaluation.TypedElementExtensions.Select(Ignixa.Abstractions.IElement, System.String, Ignixa.FhirPath.Evaluation.EvaluationContext)
       mov       rbx,rax
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Ignixa.Abstractions.IElement>
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M00_L00
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [rax+20]
M00_L00:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Collections.Generic.ICollection<Ignixa.Abstractions.IElement>
       call      qword ptr [7FF8AF60F7F8]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        short M00_L01
       mov       rdx,rax
       mov       rcx,7FF8B04B8FF0
       add       rsp,20
       pop       rbx
       jmp       qword ptr [7FF8AF92D680]; System.Linq.Enumerable.ICollectionToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.ICollection`1<System.__Canon>)
M00_L01:
       mov       rdx,rbx
       mov       rcx,7FF8B04B9078
       add       rsp,20
       pop       rbx
       jmp       qword ptr [7FF8B03870C0]; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
; Total bytes of code 146
```
```assembly
; Ignixa.FhirPath.Evaluation.TypedElementExtensions.Select(Ignixa.Abstractions.IElement, System.String, Ignixa.FhirPath.Evaluation.EvaluationContext)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0A8
       lea       rbp,[rsp+0E0]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-70],ymm4
       vmovdqa   xmmword ptr [rbp-50],xmm4
       xor       eax,eax
       mov       [rbp-40],rax
       mov       rdi,rcx
       mov       rsi,rdx
       mov       rbx,r8
       test      rdi,rdi
       je        near ptr M01_L19
       test      rsi,rsi
       je        near ptr M01_L38
       xor       r14d,r14d
       cmp       dword ptr [rsi+8],0
       jle       near ptr M01_L38
M01_L00:
       movzx     ecx,word ptr [rsi+r14*2+0C]
       cmp       ecx,100
       jae       near ptr M01_L20
       mov       rax,7FF90A792F18
       test      byte ptr [rcx+rax],80
       jne       near ptr M01_L18
M01_L01:
       test      rbx,rbx
       jne       near ptr M01_L06
       mov       rcx,offset MT_Ignixa.FhirPath.Evaluation.EvaluationContext
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,227F7C02008
       mov       r15,[rcx]
       mov       rcx,227F7C02C70
       mov       r13,[rcx]
       mov       rcx,227F7C02C60
       mov       r12,[rcx]
       mov       rcx,227F7C00070
       mov       r14,[rcx]
       mov       rcx,[r12+10]
       mov       rax,rcx
       mov       rax,[rax+10]
       mov       rdx,227F7C02C78
       test      rax,rax
       cmove     rax,[rdx]
       cmp       [rcx+8],r14
       je        near ptr M01_L26
       mov       [rbp-80],rax
       test      rax,rax
       je        near ptr M01_L25
       mov       rcx,227F7C02C90
       mov       rcx,[rcx]
       cmp       r14,[rcx+8]
       je        near ptr M01_L21
M01_L02:
       mov       rcx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>+Comparers
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-98],rax
       lea       rcx,[rax+8]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       r14,[rbp-98]
       lea       rcx,[r14+10]
       mov       rdx,[rbp-80]
       call      CORINFO_HELP_ASSIGN_REF
M01_L03:
       mov       rcx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-88],rax
       mov       rdx,r14
       test      rdx,rdx
       je        near ptr M01_L22
M01_L04:
       lea       rcx,[rax+10]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,227F7C02C98
       mov       r14,[rcx]
       mov       rax,[rbp-88]
       lea       rcx,[rax+8]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-88]
       mov       r9,[rax+10]
       mov       [rbp-70],r14
       mov       [rbp-68],r9
       mov       dword ptr [rsp+20],2
       lea       r9,[rbp-70]
       lea       rcx,[rbp-48]
       mov       r8,r12
       mov       rdx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>
       call      qword ptr [7FF8B00745B8]; System.Collections.Immutable.ImmutableDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AddRange(System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>, MutationInput<System.__Canon,System.__Canon>, KeyCollisionBehavior<System.__Canon,System.__Canon>)
       mov       r14,[rbp-48]
       mov       r12,[rbp-88]
       mov       eax,[r12+18]
       add       eax,[rbp-40]
       mov       [rbp-4C],eax
       test      r14,r14
       je        near ptr M01_L24
       cmp       [r12+8],r14
       jne       near ptr M01_L23
M01_L05:
       lea       rcx,[rbx+8]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+10]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+18]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+20]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [rbx+28],rcx
       mov       [rbx+30],rcx
M01_L06:
       mov       r12,[rbx+28]
       test      r12,r12
       jne       near ptr M01_L28
M01_L07:
       mov       rcx,offset MT_Ignixa.FhirPath.Evaluation.EvaluationContext
       cmp       [rbx],rcx
       jne       near ptr M01_L29
       mov       rcx,offset MT_Ignixa.FhirPath.Evaluation.EvaluationContext
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rdx,[rbx+8]
       lea       rcx,[r15+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbx+10]
       lea       rcx,[r15+10]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbx+18]
       lea       rcx,[r15+18]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbx+20]
       lea       rcx,[r15+20]
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+28]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbx+30]
       lea       rcx,[r15+30]
       call      CORINFO_HELP_ASSIGN_REF
M01_L08:
       mov       rdx,[rbx+28]
       test      rdx,rdx
       cmove     rdx,rdi
       lea       rcx,[r15+28]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbx+30]
       test      rdx,rdx
       cmove     rdx,rdi
       lea       rcx,[r15+30]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rbx,r15
M01_L09:
       mov       rcx,rsi
       call      qword ptr [7FF8B0074258]; Ignixa.FhirPath.Evaluation.TypedElementExtensions.CompileExpressionToAst(System.String)
       mov       r15,rax
       mov       rcx,offset MT_Ignixa.FhirPath.Evaluation.TypedElementExtensions+<>c__DisplayClass6_0
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       lea       rcx,[r14+8]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset MT_System.Func<System.String, System.Func<Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, System.Collections.Generic.IEnumerable<Ignixa.Abstractions.IElement>>>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       mov       rcx,227F7C01FE8
       mov       r13,[rcx]
       lea       rcx,[r12+8]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Ignixa.FhirPath.Evaluation.TypedElementExtensions+<>c__DisplayClass6_0.<CompileExpressionToDelegate>b__0(System.String)
       mov       [r12+18],rcx
       mov       r14,[r13+8]
       mov       rcx,[r14+8]
       cmp       byte ptr [r13+15],0
       jne       near ptr M01_L16
       mov       rdx,rsi
       mov       r11,7FF8AF5620F0
       call      qword ptr [r11]
M01_L10:
       lea       rdx,[rbp-58]
       mov       [rsp+20],rdx
       mov       rdx,r14
       mov       r8,rsi
       mov       [rbp-5C],eax
       mov       r9d,eax
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.String, System.Func<Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext, System.Collections.Generic.IEnumerable<Ignixa.Abstractions.IElement>>>
       call      qword ptr [7FF8AF9275D0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        near ptr M01_L30
M01_L11:
       mov       rax,[rbp-58]
       xor       edx,edx
       mov       [rbp-58],rdx
       test      rax,rax
       je        near ptr M01_L37
       mov       rdx,offset Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+<>c__DisplayClass5_0.<CompileChild>b__0(Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext)
       cmp       [rax+18],rdx
       jne       near ptr M01_L36
       mov       r15,[rax+8]
       mov       rax,[r15+8]
       mov       rdx,offset Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+<>c__DisplayClass19_1.<CompilePropertyAccess>b__2(Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext)
       cmp       [rax+18],rdx
       jne       near ptr M01_L31
       mov       rdx,[rax+8]
       mov       rdx,[rdx+8]
       mov       rcx,rdi
       mov       r11,7FF8AF5620F8
       call      qword ptr [r11]
       mov       r13,rax
M01_L12:
       mov       r14,[r15+18]
       test      r14,r14
       je        near ptr M01_L32
M01_L13:
       test      r13,r13
       je        near ptr M01_L35
       test      r14,r14
       je        near ptr M01_L34
       mov       rdx,r13
       mov       rcx,offset MT_Ignixa.Abstractions.IElement[]
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       test      rax,rax
       jne       short M01_L17
M01_L14:
       mov       rcx,offset MT_System.Linq.Enumerable+SelectManySingleSelectorIterator<Ignixa.Abstractions.IElement, Ignixa.Abstractions.IElement>
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r15+10],eax
       lea       rcx,[r15+18]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+20]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r15
M01_L15:
       add       rsp,0A8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M01_L16:
       lea       rcx,[rsi+0C]
       mov       edx,[rsi+8]
       add       edx,edx
       mov       r8d,0C976C5C6
       mov       r9d,0E95514C3
       call      qword ptr [7FF8AFAA5F98]; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       jmp       near ptr M01_L10
M01_L17:
       cmp       dword ptr [rax+8],0
       jne       short M01_L14
       jmp       near ptr M01_L33
M01_L18:
       inc       r14d
       cmp       [rsi+8],r14d
       jg        near ptr M01_L00
       jmp       near ptr M01_L38
M01_L19:
       mov       ecx,207C
       mov       rdx,7FF8B0065AA8
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045D0B0]
       int       3
M01_L20:
       call      qword ptr [7FF8B02F4150]; System.Globalization.CharUnicodeInfo.GetIsWhiteSpace(Char)
       test      eax,eax
       jne       short M01_L18
       jmp       near ptr M01_L01
M01_L21:
       mov       r8,227F7C02C90
       mov       r8,[r8]
       mov       rax,[rbp-80]
       cmp       rax,[r8+10]
       mov       [rbp-80],rax
       jne       near ptr M01_L02
       mov       r8,227F7C02C90
       mov       r14,[r8]
       jmp       near ptr M01_L03
M01_L22:
       mov       r8,227F7C02C78
       mov       r8,[r8]
       mov       rdx,227F7C00048
       mov       rdx,[rdx]
       mov       rcx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>+Comparers
       call      qword ptr [7FF8B0074498]; System.Collections.Immutable.ImmutableDictionary`2+Comparers[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Get(System.Collections.Generic.IEqualityComparer`1<System.__Canon>, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       rdx,rax
       mov       rax,[rbp-88]
       jmp       near ptr M01_L04
M01_L23:
       cmp       qword ptr [r14+8],0
       je        short M01_L24
       mov       rcx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0A0],rax
       mov       r8,[r12+10]
       mov       rcx,rax
       mov       rdx,r14
       mov       r9d,[rbp-4C]
       call      qword ptr [7FF8B045D890]
       mov       r12,[rbp-0A0]
       jmp       near ptr M01_L05
M01_L24:
       mov       rcx,r12
       call      qword ptr [7FF8B045DBF0]
       mov       r12,rax
       jmp       near ptr M01_L05
M01_L25:
       mov       ecx,511
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
M01_L26:
       cmp       [rcx+10],rax
       jne       short M01_L27
       jmp       near ptr M01_L05
M01_L27:
       mov       rdx,rax
       call      qword ptr [7FF8B045D878]
       mov       r14,rax
       mov       rcx,offset MT_System.Collections.Immutable.ImmutableDictionary<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-90],rax
       mov       r9d,[r12+18]
       mov       rdx,[r12+8]
       mov       rcx,rax
       mov       r8,r14
       call      qword ptr [7FF8B045D890]
       mov       r12,[rbp-90]
       jmp       near ptr M01_L05
M01_L28:
       cmp       qword ptr [rbx+30],0
       jne       near ptr M01_L09
       jmp       near ptr M01_L07
M01_L29:
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+38]
       mov       r15,rax
       jmp       near ptr M01_L08
M01_L30:
       mov       edx,[rbp-5C]
       mov       byte ptr [rbp-78],1
       mov       [rbp-74],edx
       mov       rdx,rsi
       mov       rcx,[r12+8]
       call      qword ptr [r12+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-58]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-78]
       mov       r8,rsi
       mov       rdx,r14
       mov       rcx,r13
       call      qword ptr [7FF8AF92DEA8]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       near ptr M01_L11
M01_L31:
       mov       rdx,rdi
       mov       r8,rbx
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       r13,rax
       jmp       near ptr M01_L12
M01_L32:
       mov       rcx,offset MT_System.Func<Ignixa.Abstractions.IElement, System.Collections.Generic.IEnumerable<Ignixa.Abstractions.IElement>>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,r14
       mov       rdx,r15
       mov       r8,7FF8B0161FC8
       call      qword ptr [7FF8AF6069D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       lea       rcx,[r15+18]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M01_L13
M01_L33:
       mov       rcx,227F7C03088
       mov       rax,[rcx]
       jmp       near ptr M01_L15
M01_L34:
       mov       ecx,10
       call      qword ptr [7FF8AF60F738]
       int       3
M01_L35:
       mov       ecx,11
       call      qword ptr [7FF8AF60F738]
       int       3
M01_L36:
       mov       rdx,rdi
       mov       r8,rbx
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       jmp       near ptr M01_L15
M01_L37:
       mov       rcx,227F7C02000
       mov       rcx,[rcx]
       mov       rdx,rdi
       mov       r8,r15
       mov       r9,rbx
       call      qword ptr [7FF8B0074288]
       nop
       add       rsp,0A8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M01_L38:
       mov       ecx,15F5
       mov       rdx,7FF8B0065AA8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rsi
       call      qword ptr [7FF8B045D740]
       int       3
; Total bytes of code 1869
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M02_L02
       cmp       [rdx],rcx
       je        short M02_L02
       mov       rax,[rdx]
       mov       rax,[rax+10]
M02_L00:
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       jmp       short M02_L00
M02_L01:
       xor       edx,edx
M02_L02:
       mov       rax,rdx
       ret
; Total bytes of code 81
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rdx,rdx
       je        short M03_L04
       mov       rax,[rdx]
       movzx     r8d,word ptr [rax+0E]
       test      r8,r8
       je        short M03_L02
       mov       r10,[rax+38]
       cmp       r8,4
       jl        short M03_L01
M03_L00:
       cmp       [r10],rcx
       je        short M03_L04
       cmp       [r10+8],rcx
       je        short M03_L04
       cmp       [r10+10],rcx
       je        short M03_L04
       cmp       [r10+18],rcx
       je        short M03_L04
       add       r10,20
       add       r8,0FFFFFFFFFFFFFFFC
       cmp       r8,4
       jge       short M03_L00
       test      r8,r8
       je        short M03_L02
M03_L01:
       cmp       [r10],rcx
       je        short M03_L04
       add       r10,8
       dec       r8
       test      r8,r8
       jg        short M03_L01
M03_L02:
       test      dword ptr [rax],504C0000
       je        short M03_L03
       jmp       qword ptr [7FF8AF92F0A8]; System.Runtime.CompilerServices.CastHelpers.IsInstance_Helper(Void*, System.Object)
M03_L03:
       xor       edx,edx
M03_L04:
       mov       rax,rdx
       ret
; Total bytes of code 107
```
```assembly
; System.Linq.Enumerable.ICollectionToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.ICollection`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rcx,rbx
       call      qword ptr [7FF91AA3F030]
       mov       rcx,rsi
       mov       r11,rax
       call      qword ptr [rax]
       mov       edi,eax
       test      edi,edi
       je        short M04_L00
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E510]
       mov       rcx,rax
       movsxd    rdx,edi
       call      qword ptr [7FF91AA3C678]; CORINFO_HELP_NEWARR_1_DIRECT
       mov       rdi,rax
       mov       rcx,rbx
       call      qword ptr [7FF91AA3F038]
       mov       rcx,rsi
       mov       r11,rax
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [rax]
       mov       rax,rdi
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M04_L00:
       mov       rcx,rbx
       call      qword ptr [7FF91AA3EC78]
       mov       rcx,rax
       lea       rax,[System.Linq.Utilities.CombineSelectors[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Func`2<System.__Canon,System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [rax]
; Total bytes of code 128
```
```assembly
; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,180
       xorps     xmm4,xmm4
       mov       rax,0FFFFFFFFFFFFFEB0
M05_L00:
       movaps    [rsp+rax+170],xmm4
       movaps    [rsp+rax+180],xmm4
       movaps    [rsp+rax+190],xmm4
       add       rax,30
       jne       short M05_L00
       mov       [rsp+170],rax
       mov       [rsp+178],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M05_L03
       xorps     xmm0,xmm0
       movups    [rsp+138],xmm0
       movups    [rsp+148],xmm0
       movups    [rsp+158],xmm0
       movups    [rsp+168],xmm0
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E5A8]
       mov       rdi,rax
       call      qword ptr [7FF91AA3C6B8]
       mov       rcx,rdi
       call      qword ptr [7FF91AA3ED98]
       mov       rdx,rax
       lea       rcx,[rsp+20]
       lea       r8,[rsp+138]
       mov       r9d,8
       call      qword ptr [7FF91AA3FBD8]; Precode of System.Runtime.InteropServices.MemoryMarshal.CreateSpan[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Int32)
       lea       rcx,[rsp+40]
       mov       edx,0D8
       call      qword ptr [7FF91AA3C660]; Precode of System.SpanHelpers.ClearWithoutReferences(Byte ByRef, UIntPtr)
       xor       ecx,ecx
       mov       [rsp+30],ecx
       mov       [rsp+34],ecx
       mov       [rsp+38],ecx
       movups    xmm0,[rsp+20]
       movups    [rsp+118],xmm0
       movups    xmm0,[rsp+20]
       movups    [rsp+128],xmm0
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E488]
       mov       rbx,rax
       mov       rdx,rbx
       lea       rcx,[rsp+30]
       mov       r8,rsi
       call      qword ptr [7FF91AA402B0]
       mov       rdx,rbx
       lea       rcx,[rsp+30]
       call      qword ptr [7FF91AA402B8]; Precode of System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].ToArray()
       mov       rsi,rax
       mov       rcx,rbx
       mov       ebx,[rsp+30]
       test      ebx,ebx
       jne       short M05_L02
M05_L01:
       mov       rax,rsi
       add       rsp,180
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M05_L02:
       call      qword ptr [7FF91AA3F0A8]
       mov       rdx,rax
       lea       rcx,[rsp+30]
       mov       r8d,ebx
       call      qword ptr [7FF91AA40278]; Precode of System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].ReturnArrays(Int32)
       jmp       short M05_L01
M05_L03:
       mov       ecx,11
       call      qword ptr [7FF91AA3FF38]
       int       3
; Total bytes of code 338
```
```assembly
; System.Collections.Immutable.ImmutableDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AddRange(System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>, MutationInput<System.__Canon,System.__Canon>, KeyCollisionBehavior<System.__Canon,System.__Canon>)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0B8
       lea       rbp,[rsp+0F0]
       xor       eax,eax
       mov       [rbp-88],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-80],ymm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       mov       [rbp-0A8],rsp
       mov       [rbp-40],rdx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       r14,[r9]
       mov       r15,[r9+8]
       test      rdi,rdi
       je        near ptr M06_L14
       xor       r13d,r13d
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       mov       r11,[rcx+0C8]
       test      r11,r11
       je        short M06_L01
M06_L00:
       mov       rcx,rdi
       call      qword ptr [r11]
       mov       [rbp-90],rax
       jmp       short M06_L02
M06_L01:
       mov       rcx,rsi
       mov       rdx,7FF8B042CEE8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M06_L00
M06_L02:
       mov       rcx,offset MT_System.SZGenericArrayEnumerator<System.Collections.Generic.KeyValuePair<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>>
       cmp       [rax],rcx
       jne       short M06_L04
       mov       r8d,[rax+8]
       inc       r8d
       mov       ecx,[rax+0C]
       cmp       r8d,ecx
       jb        short M06_L03
       mov       [rax+8],ecx
       jmp       near ptr M06_L13
M06_L03:
       mov       [rax+8],r8d
       jmp       short M06_L05
M06_L04:
       mov       rcx,rax
       mov       r11,7FF8AF562100
       call      qword ptr [r11]
       test      eax,eax
       mov       rax,[rbp-90]
       je        near ptr M06_L15
M06_L05:
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       mov       r11,[rcx+0D0]
       test      r11,r11
       je        short M06_L06
       jmp       short M06_L07
M06_L06:
       mov       rcx,rsi
       mov       rdx,7FF8B042CF08
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       mov       rax,[rbp-90]
M06_L07:
       lea       rdx,[rbp-50]
       mov       rcx,rax
       call      qword ptr [r11]
       mov       rdi,[rbp-50]
       test      rdi,rdi
       je        near ptr M06_L12
       mov       r12,[r15+8]
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       mov       r11,[rcx+0E8]
       test      r11,r11
       je        short M06_L08
       jmp       short M06_L09
M06_L08:
       mov       rcx,rsi
       mov       rdx,7FF8B042D090
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M06_L09:
       mov       rcx,r12
       mov       rdx,rdi
       call      qword ptr [r11]
       mov       r12d,eax
       lea       rdx,[rbp-68]
       mov       rcx,r14
       mov       r8d,r12d
       cmp       [rcx],ecx
       call      qword ptr [7FF8B045DEF0]
       mov       rax,[rbp-48]
       mov       [rbp-98],rax
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       mov       r8,[rcx+0F0]
       test      r8,r8
       je        short M06_L10
       jmp       short M06_L11
M06_L10:
       mov       rcx,rsi
       mov       rdx,7FF8B042D0A8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r8,rax
M06_L11:
       mov       rax,[rbp-98]
       mov       [rsp+20],rax
       mov       [rsp+28],r15
       mov       rcx,[r15+10]
       mov       [rsp+30],rcx
       mov       eax,[rbp+30]
       mov       [rsp+38],eax
       lea       rcx,[rbp-70]
       mov       [rsp+40],rcx
       lea       rcx,[rbp-68]
       lea       rdx,[rbp-88]
       mov       r9,rdi
       call      qword ptr [7FF8B045D9F8]
       mov       [rsp+20],r15
       lea       r9,[rbp-88]
       mov       rcx,rsi
       mov       rdx,r14
       mov       r8d,r12d
       call      qword ptr [7FF8B045DF08]
       mov       r14,rax
       cmp       dword ptr [rbp-70],1
       mov       rax,[rbp-90]
       jne       near ptr M06_L02
       inc       r13d
       jmp       near ptr M06_L02
M06_L12:
       mov       ecx,717
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
M06_L13:
       test      r14,r14
       je        short M06_L16
       mov       rdx,r14
       mov       rcx,rbx
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       [rbx+8],r13d
       mov       rax,rbx
       add       rsp,0B8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M06_L14:
       mov       ecx,40B
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
M06_L15:
       mov       rcx,rax
       mov       r11,7FF8AF562108
       call      qword ptr [r11]
       jmp       short M06_L13
M06_L16:
       mov       ecx,4AB
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,58
       mov       rbp,[rcx+48]
       mov       [rsp+48],rbp
       lea       rbp,[rbp+0F0]
       cmp       qword ptr [rbp-90],0
       je        short M06_L17
       mov       rcx,offset MT_System.SZGenericArrayEnumerator<System.Collections.Generic.KeyValuePair<System.String, System.Collections.Immutable.ImmutableList<Ignixa.Abstractions.IElement>>>
       mov       rax,[rbp-90]
       cmp       [rax],rcx
       je        short M06_L17
       mov       rcx,rax
       mov       r11,7FF8AF562108
       call      qword ptr [r11]
M06_L17:
       nop
       add       rsp,58
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 788
```
```assembly
; Ignixa.FhirPath.Evaluation.TypedElementExtensions.CompileExpressionToAst(System.String)
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,58
       lea       rbp,[rsp+80]
       xor       eax,eax
       mov       [rbp-30],rax
       mov       rbx,rcx
       mov       rcx,227F7C02CC0
       mov       rsi,[rcx]
       mov       rcx,227F7C01FE0
       mov       rdi,[rcx]
       test      rsi,rsi
       je        near ptr M07_L04
M07_L00:
       test      rbx,rbx
       je        near ptr M07_L07
       test      rsi,rsi
       je        near ptr M07_L06
       mov       r14,[rdi+8]
       mov       rcx,[r14+8]
       cmp       byte ptr [rdi+15],0
       jne       short M07_L03
       mov       rdx,rbx
       mov       r11,7FF8AF562110
       call      qword ptr [r11]
       mov       r15d,eax
M07_L01:
       lea       rdx,[rbp-30]
       mov       [rsp+20],rdx
       mov       rdx,r14
       mov       r8,rbx
       mov       r9d,r15d
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.String, Ignixa.FhirPath.Expressions.Expression>
       call      qword ptr [7FF8AF9275D0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        short M07_L05
M07_L02:
       mov       rax,[rbp-30]
       add       rsp,58
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
M07_L03:
       lea       rcx,[rbx+0C]
       mov       edx,[rbx+8]
       add       edx,edx
       mov       r8d,0C976C5C6
       mov       r9d,0E95514C3
       call      qword ptr [7FF8AFAA5F98]; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       mov       r15d,eax
       jmp       short M07_L01
M07_L04:
       mov       rcx,offset MT_System.Func<System.String, Ignixa.FhirPath.Expressions.Expression>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,227F7C02CB8
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Ignixa.FhirPath.Evaluation.TypedElementExtensions+<>c.<CompileExpressionToAst>b__5_0(System.String)
       call      qword ptr [7FF8AF6069D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,227F7C02CC0
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M07_L00
M07_L05:
       mov       byte ptr [rbp-38],1
       mov       [rbp-34],r15d
       mov       rdx,rbx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-30]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-38]
       mov       r8,rbx
       mov       rdx,r14
       mov       rcx,rdi
       call      qword ptr [7FF8AF92DEA8]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       near ptr M07_L02
M07_L06:
       mov       ecx,0C00
       mov       rdx,7FF8AF957C50
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8AFB072A0]
       int       3
M07_L07:
       mov       ecx,1
       mov       rdx,7FF8AF957C50
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8AFB072A0]
       int       3
; Total bytes of code 407
```
```assembly
; Ignixa.FhirPath.Evaluation.TypedElementExtensions+<>c__DisplayClass6_0.<CompileExpressionToDelegate>b__0(System.String)
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       rax,[rbp+10]
       mov       rdx,[rax+8]
       mov       rax,227F7C01FF8
       mov       rcx,[rax]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0165C20]; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler.TryCompile(Ignixa.FhirPath.Expressions.Expression)
       nop
       add       rsp,20
       pop       rbp
       ret
; Total bytes of code 54
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rdi,r8
       mov       esi,r9d
       mov       rbp,[rdx+8]
       mov       rcx,[rdx+10]
       mov       eax,esi
       imul      rax,[rdx+28]
       shr       rax,20
       inc       rax
       mov       edx,[rcx+8]
       mov       r8d,edx
       imul      rax,r8
       shr       rax,20
       cmp       eax,edx
       jae       near ptr M09_L05
       mov       edx,eax
       mov       r14,[rcx+rdx*8+10]
       test      r14,r14
       je        short M09_L04
M09_L00:
       cmp       esi,[r14+20]
       jne       short M09_L03
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       r11,[rcx+0B0]
       test      r11,r11
       je        short M09_L02
M09_L01:
       mov       rdx,[r14+8]
       mov       rcx,rbp
       mov       r8,rdi
       call      qword ptr [r11]
       test      eax,eax
       je        short M09_L03
       mov       rdx,[r14+10]
       mov       rcx,[rsp+80]
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       eax,1
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M09_L02:
       mov       rcx,rbx
       mov       rdx,7FF8B0425DE0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M09_L01
M09_L03:
       mov       r14,[r14+18]
       test      r14,r14
       jne       short M09_L00
M09_L04:
       xor       eax,eax
       mov       rbx,[rsp+80]
       mov       [rbx],rax
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M09_L05:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 217
```
```assembly
; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+<>c__DisplayClass5_0.<CompileChild>b__0(Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rcx,rdx
       mov       rsi,[rbx+8]
       mov       rdx,offset Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+<>c__DisplayClass19_1.<CompilePropertyAccess>b__2(Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext)
       cmp       [rsi+18],rdx
       jne       near ptr M10_L05
       mov       rdx,[rsi+8]
       mov       rdx,[rdx+8]
       mov       r11,7FF8AF562040
       call      qword ptr [r11]
       mov       rdi,rax
M10_L00:
       mov       rbp,[rbx+18]
       test      rbp,rbp
       je        near ptr M10_L06
M10_L01:
       test      rdi,rdi
       je        near ptr M10_L09
       test      rbp,rbp
       je        near ptr M10_L08
       mov       rdx,rdi
       mov       rcx,offset MT_Ignixa.Abstractions.IElement[]
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       test      rax,rax
       jne       short M10_L04
M10_L02:
       mov       rcx,offset MT_System.Linq.Enumerable+SelectManySingleSelectorIterator<Ignixa.Abstractions.IElement, Ignixa.Abstractions.IElement>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r14+10],eax
       lea       rcx,[r14+18]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r14+20]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r14
M10_L03:
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M10_L04:
       cmp       dword ptr [rax+8],0
       jne       short M10_L02
       jmp       short M10_L07
M10_L05:
       mov       rdx,rcx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       mov       rdi,rax
       jmp       near ptr M10_L00
M10_L06:
       mov       rcx,offset MT_System.Func<Ignixa.Abstractions.IElement, System.Collections.Generic.IEnumerable<Ignixa.Abstractions.IElement>>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       rcx,rbp
       mov       rdx,rbx
       mov       r8,7FF8B0161FC8
       call      qword ptr [7FF8AF6069D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       lea       rcx,[rbx+18]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M10_L01
M10_L07:
       mov       rcx,227F7C03088
       mov       rax,[rcx]
       jmp       short M10_L03
M10_L08:
       mov       ecx,10
       call      qword ptr [7FF8AF60F738]
       int       3
M10_L09:
       mov       ecx,11
       call      qword ptr [7FF8AF60F738]
       int       3
; Total bytes of code 305
```
```assembly
; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+<>c__DisplayClass19_1.<CompilePropertyAccess>b__2(Ignixa.Abstractions.IElement, Ignixa.FhirPath.Evaluation.EvaluationContext)
       mov       r11,rdx
       mov       rdx,[rcx+8]
       mov       rcx,r11
       mov       r11,7FF8AF562048
       cmp       [rcx],ecx
       jmp       qword ptr [r11]
; Total bytes of code 25
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       push      rsi
       push      rbx
       test      rdx,rdx
       je        short M12_L02
       mov       r8,[rdx]
       cmp       r8,rcx
       je        short M12_L02
       mov       rax,227F7C00038
       mov       r10,[rax]
       add       r10,10
       rorx      rax,r8,20
       xor       rax,rcx
       mov       r9,9E3779B97F4A7C15
       imul      rax,r9
       mov       r9d,[r10]
       shrx      r9,rax,r9
       xor       r11d,r11d
M12_L00:
       lea       eax,[r9+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[r10+rax*8]
       mov       ebx,[rax]
       mov       rsi,[rax+8]
       and       ebx,0FFFFFFFE
       cmp       rsi,r8
       jne       short M12_L03
       mov       rsi,rcx
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M12_L03
       cmp       ebx,[rax]
       jne       short M12_L04
M12_L01:
       cmp       esi,1
       je        short M12_L02
       test      esi,esi
       jne       short M12_L05
       xor       edx,edx
M12_L02:
       mov       rax,rdx
       pop       rbx
       pop       rsi
       ret
M12_L03:
       test      ebx,ebx
       je        short M12_L04
       inc       r11d
       add       r9d,r11d
       and       r9d,[r10+4]
       cmp       r11d,8
       jl        short M12_L00
M12_L04:
       mov       esi,2
       jmp       short M12_L01
M12_L05:
       pop       rbx
       pop       rsi
       jmp       near ptr System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny_NoCacheLookup(Void*, System.Object)
; Total bytes of code 162
```
```assembly
; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       cmp       edx,8
       jb        short M13_L01
       mov       eax,edx
       shr       eax,3
M13_L00:
       add       r8d,[rcx]
       mov       r10d,[rcx+4]
       xor       r9d,r8d
       rol       r8d,14
       add       r8d,r9d
       rol       r9d,9
       xor       r9d,r8d
       rol       r8d,1B
       add       r8d,r9d
       rol       r9d,13
       add       r10d,r8d
       mov       r8d,r9d
       xor       r8d,r10d
       rol       r10d,14
       add       r10d,r8d
       rol       r8d,9
       xor       r8d,r10d
       rol       r10d,1B
       add       r10d,r8d
       rol       r8d,13
       mov       r9d,r8d
       add       rcx,8
       dec       eax
       mov       r8d,r10d
       jne       short M13_L00
       test      dl,4
       je        short M13_L03
       jmp       short M13_L02
M13_L01:
       cmp       edx,4
       jb        short M13_L05
M13_L02:
       add       r8d,[rcx]
       xor       r9d,r8d
       rol       r8d,14
       add       r8d,r9d
       rol       r9d,9
       xor       r9d,r8d
       rol       r8d,1B
       add       r8d,r9d
       rol       r9d,13
M13_L03:
       mov       eax,edx
       and       rax,7
       mov       eax,[rcx+rax-4]
       shr       eax,8
       or        eax,80000000
       mov       ecx,edx
       not       ecx
       shl       ecx,3
       shr       eax,cl
M13_L04:
       add       eax,r8d
       mov       edx,r9d
       xor       edx,eax
       rol       eax,14
       add       eax,edx
       rol       edx,9
       xor       edx,eax
       rol       eax,1B
       add       eax,edx
       rol       edx,13
       mov       r8d,edx
       xor       r8d,eax
       rol       eax,14
       add       eax,r8d
       rol       r8d,9
       xor       r8d,eax
       rol       eax,1B
       add       eax,r8d
       mov       r9d,r8d
       rol       r9d,13
       xor       eax,r9d
       ret
M13_L05:
       mov       eax,80
       test      dl,1
       jne       short M13_L07
M13_L06:
       test      dl,2
       je        short M13_L04
       shl       eax,10
       movzx     edx,word ptr [rcx]
       or        eax,edx
       jmp       short M13_L04
M13_L07:
       mov       eax,edx
       and       rax,2
       movzx     eax,byte ptr [rcx+rax]
       or        eax,8000
       jmp       short M13_L06
; Total bytes of code 267
```
```assembly
; System.Globalization.CharUnicodeInfo.GetIsWhiteSpace(Char)
       sub       rsp,28
       movzx     ecx,cx
       call      qword ptr [7FF90B387540]; Precode of System.Globalization.CharUnicodeInfo.GetCategoryCasingTableOffsetNoBoundsChecks(UInt32)
       mov       rcx,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       cmp       byte ptr [rcx+rax],0
       setl      al
       movzx     eax,al
       add       rsp,28
       ret
; Total bytes of code 35
```
```assembly
; System.Collections.Immutable.ImmutableDictionary`2+Comparers[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Get(System.Collections.Generic.IEqualityComparer`1<System.__Canon>, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        near ptr M15_L03
       test      rdi,rdi
       je        short M15_L02
       mov       rcx,rbx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rcx,[rax]
       cmp       [rcx+8],rsi
       je        short M15_L01
M15_L00:
       mov       rcx,rbx
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+10]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,rbx
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M15_L01:
       mov       rcx,rbx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rcx,[rax]
       cmp       [rcx+10],rdi
       jne       short M15_L00
       mov       rcx,rbx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,[rax]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M15_L02:
       mov       ecx,511
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
M15_L03:
       mov       ecx,71F
       mov       rdx,7FF8B0067838
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B045CE28]
       int       3
; Total bytes of code 194
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,60
       lea       rbp,[rsp+90]
       xor       eax,eax
       mov       [rbp-58],rax
       mov       [rbp-70],rsp
       mov       [rbp-38],rcx
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       [rbp+20],r8
       mov       [rbp+28],r9
       mov       rbx,[rbp+30]
       mov       r8,[rbp+18]
       mov       r8,[r8+8]
       mov       [rbp-58],r8
       movzx     r8d,byte ptr [rbp+28]
       mov       esi,[rbp+2C]
       test      r8d,r8d
       jne       near ptr M16_L03
       cmp       byte ptr [rcx+15],0
       jne       near ptr M16_L02
       mov       rcx,[rcx]
       call      qword ptr [7FF924A7FB30]
       mov       rcx,[rbp-58]
       mov       r11,rax
       mov       rdx,[rbp+20]
       call      qword ptr [rax]
M16_L00:
       mov       [rbp-3C],eax
M16_L01:
       mov       rax,[rbp+18]
       mov       rcx,[rax+18]
       mov       [rbp-60],rcx
       mov       r8,[rbp+10]
       cmp       [r8],r8d
       mov       rax,[rbp+18]
       mov       r10,[rax+10]
       mov       rax,[rbp+18]
       mov       r9d,[rbp-3C]
       imul      r9,[rax+28]
       shr       r9,20
       inc       r9
       mov       r11d,[r10+8]
       mov       ebx,r11d
       imul      r9,rbx
       shr       r9,20
       mov       eax,r9d
       xor       edx,edx
       div       dword ptr [rcx+8]
       mov       [rbp-40],edx
       cmp       r9d,r11d
       jae       near ptr M16_L27
       mov       ecx,r9d
       lea       rbx,[r10+rcx*8+10]
       xor       esi,esi
       xor       edi,edi
       xor       ecx,ecx
       mov       [rbp-48],ecx
       jmp       short M16_L04
M16_L02:
       mov       rdx,[rbp+20]
       mov       rcx,rdx
       lea       r11,[System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)]
       cmp       [rcx],ecx
       call      qword ptr [r11]
       jmp       short M16_L00
M16_L03:
       mov       eax,esi
       jmp       near ptr M16_L00
M16_L04:
       cmp       byte ptr [rbp+40],0
       je        short M16_L05
       mov       rcx,[rbp-60]
       mov       ecx,[rcx+8]
       cmp       [rbp-40],ecx
       jae       near ptr M16_L17
       mov       rcx,[rbp-60]
       mov       edx,[rbp-40]
       mov       rcx,[rcx+rdx*8+10]
       lea       rdx,[rbp-48]
       call      qword ptr [7FF924A7FFC8]; Precode of System.Threading.Monitor.Enter(System.Object, Boolean ByRef)
M16_L05:
       mov       rcx,[rbp+18]
       mov       r8,[rbp+10]
       cmp       rcx,[r8+8]
       jne       near ptr M16_L14
       xor       r14d,r14d
       mov       r15,[rbx]
       test      r15,r15
       je        short M16_L08
M16_L06:
       mov       ecx,[rbp-3C]
       cmp       ecx,[r15+20]
       jne       short M16_L07
       mov       rcx,[r8]
       call      qword ptr [7FF924A7F678]
       mov       rcx,rax
       call      qword ptr [7FF924A7FD40]
       mov       rdx,[r15+8]
       mov       rcx,[rbp-58]
       mov       r11,rax
       mov       r8,[rbp+20]
       call      qword ptr [rax]
       test      eax,eax
       mov       r8,[rbp+10]
       jne       near ptr M16_L10
M16_L07:
       inc       r14d
       mov       r15,[r15+18]
       test      r15,r15
       jne       short M16_L06
M16_L08:
       mov       rcx,[r8]
       call      qword ptr [7FF924A7F6D0]
       mov       rcx,rax
       call      qword ptr [7FF924A7F2B0]; CORINFO_HELP_NEWFAST
       mov       r15,rax
       mov       r13,[rbx]
       lea       rcx,[r15+8]
       mov       rdx,[rbp+20]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+10]
       mov       rdx,[rbp+30]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+18]
       mov       rdx,r13
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       ecx,[rbp-3C]
       mov       [r15+20],ecx
       mov       rcx,rbx
       mov       rdx,r15
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+18]
       mov       rcx,[rcx+20]
       mov       edx,[rcx+8]
       cmp       [rbp-40],edx
       jae       near ptr M16_L17
       mov       edx,[rbp-40]
       lea       rcx,[rcx+rdx*4+10]
       mov       edx,[rcx]
       add       edx,1
       jo        near ptr M16_L19
       mov       [rcx],edx
       mov       r8,[rbp+10]
       cmp       edx,[r8+10]
       jg        short M16_L13
M16_L09:
       cmp       r14d,64
       jbe       near ptr M16_L20
       jmp       near ptr M16_L18
M16_L10:
       cmp       byte ptr [rbp+38],0
       jne       short M16_L12
       mov       rdx,[r15+10]
       mov       rcx,[rbp+48]
       call      qword ptr [7FF924A7F290]; CORINFO_HELP_CHECKED_ASSIGN_REF
M16_L11:
       xor       ecx,ecx
       mov       [rbp-4C],ecx
       jmp       near ptr M16_L25
M16_L12:
       lea       rcx,[r15+10]
       mov       rdx,[rbp+30]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+48]
       mov       rdx,[rbp+30]
       call      qword ptr [7FF924A7F290]; CORINFO_HELP_CHECKED_ASSIGN_REF
       jmp       short M16_L11
M16_L13:
       mov       esi,1
       jmp       short M16_L09
M16_L14:
       mov       rcx,[r8+8]
       mov       [rbp+18],rcx
       mov       rcx,[rbp-58]
       mov       rax,[rbp+18]
       cmp       rcx,[rax+8]
       je        near ptr M16_L26
       mov       rcx,[rbp+18]
       mov       rcx,[rcx+8]
       mov       [rbp-58],rcx
       mov       r8,[rbp+10]
       cmp       byte ptr [r8+15],0
       jne       short M16_L15
       mov       rcx,[r8]
       call      qword ptr [7FF924A7FB30]
       mov       rcx,[rbp-58]
       mov       r11,rax
       mov       rdx,[rbp+20]
       call      qword ptr [rax]
       jmp       short M16_L16
M16_L15:
       mov       rcx,[rbp+20]
       lea       r11,[System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)]
       cmp       [rcx],ecx
       call      qword ptr [r11]
M16_L16:
       mov       [rbp-3C],eax
       jmp       near ptr M16_L26
M16_L17:
       call      qword ptr [7FF924A7F280]
       int       3
M16_L18:
       mov       rcx,[rbp-58]
       call      qword ptr [7FF924A7FE80]
       mov       ecx,1
       test      rax,rax
       cmovne    edi,ecx
       jmp       short M16_L20
M16_L19:
       call      qword ptr [7FF924A7F278]
       int       3
M16_L20:
       mov       r8,[rbp+10]
       cmp       byte ptr [rbp-48],0
       je        short M16_L21
       mov       rcx,[rbp-60]
       mov       ecx,[rcx+8]
       cmp       [rbp-40],ecx
       jae       near ptr M16_L27
       mov       rcx,[rbp-60]
       mov       eax,[rbp-40]
       mov       rcx,[rcx+rax*8+10]
       call      qword ptr [7FF924A7FFD0]; System.Threading.Monitor.Exit(System.Object)
       mov       r8,[rbp+10]
M16_L21:
       mov       ecx,esi
       or        ecx,edi
       jne       short M16_L23
M16_L22:
       mov       rcx,[rbp+48]
       mov       rdx,[rbp+30]
       call      qword ptr [7FF924A7F290]; CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       eax,1
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L23:
       mov       rcx,r8
       mov       rdx,[rbp+18]
       mov       r8d,esi
       mov       r9d,edi
       call      qword ptr [7FF924A80808]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GrowTable(Tables<System.__Canon,System.__Canon>, Boolean, Boolean)
       jmp       short M16_L22
M16_L24:
       mov       eax,[rbp-4C]
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L25:
       mov       rcx,rsp
       call      M16_L28
       jmp       short M16_L24
M16_L26:
       mov       rcx,rsp
       call      M16_L28
       jmp       near ptr M16_L01
M16_L27:
       call      qword ptr [7FF924A7F280]
       int       3
M16_L28:
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+90]
       cmp       byte ptr [rbp-48],0
       je        short M16_L29
       mov       rcx,[rbp-60]
       mov       ecx,[rcx+8]
       cmp       [rbp-40],ecx
       jae       short M16_L30
       mov       rcx,[rbp-60]
       mov       eax,[rbp-40]
       mov       rcx,[rcx+rax*8+10]
       call      qword ptr [7FF924A7FFD0]; System.Threading.Monitor.Exit(System.Object)
M16_L29:
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L30:
       call      qword ptr [7FF924A7F280]
       int       3
; Total bytes of code 987
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,r8
       test      rdx,rdx
       je        short M17_L00
       lea       rcx,[rbx+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rsi
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M17_L00:
       call      qword ptr [7FF8B045DC08]
       int       3
; Total bytes of code 44
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstance_Helper(Void*, System.Object)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rax,227F7C00038
       mov       r8,[rax]
       mov       r10,[rdx]
       add       r8,10
       rorx      rax,r10,20
       xor       rax,rcx
       mov       r9,9E3779B97F4A7C15
       imul      rax,r9
       mov       r9d,[r8]
       shrx      r9,rax,r9
       xor       r11d,r11d
M18_L00:
       lea       eax,[r9+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[r8+rax*8]
       mov       ebx,[rax]
       mov       rsi,[rax+8]
       and       ebx,0FFFFFFFE
       cmp       rsi,r10
       jne       short M18_L02
       mov       rsi,rcx
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M18_L02
       cmp       ebx,[rax]
       jne       short M18_L03
M18_L01:
       cmp       esi,1
       jne       short M18_L04
       mov       rax,rdx
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M18_L02:
       test      ebx,ebx
       je        short M18_L03
       inc       r11d
       add       r9d,r11d
       and       r9d,[r8+4]
       cmp       r11d,8
       jl        short M18_L00
M18_L03:
       mov       esi,2
       jmp       short M18_L01
M18_L04:
       test      esi,esi
       jne       short M18_L05
       xor       eax,eax
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M18_L05:
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny_NoCacheLookup(Void*, System.Object)
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 173
```
```assembly
; System.Linq.Utilities.CombineSelectors[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Func`2<System.__Canon,System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E4C0]
       mov       rcx,rax
       call      qword ptr [7FF91AA3C670]; CORINFO_HELP_NEWFAST
       mov       rbp,rax
       lea       rcx,[rbp+8]
       mov       rdx,rdi
       call      qword ptr [7FF91AA3C640]; CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbp+10]
       mov       rdx,rsi
       call      qword ptr [7FF91AA3C640]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E4C8]
       mov       rcx,rax
       call      qword ptr [7FF91AA3C670]; CORINFO_HELP_NEWFAST
       mov       rbx,rax
       mov       rcx,rbx
       mov       rdx,rbp
       call      qword ptr [7FF91AA3F5E0]
       mov       rax,rbx
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 114
```
```assembly
; Ignixa.FhirPath.Evaluation.TypedElementExtensions+<>c.<CompileExpressionToAst>b__5_0(System.String)
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       rax,227F7C01FF0
       mov       rcx,[rax]
       mov       rdx,[rbp+18]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0077E70]; Ignixa.FhirPath.Parser.FhirPathParser.Parse(System.String)
       nop
       add       rsp,20
       pop       rbp
       ret
; Total bytes of code 50
```
```assembly
; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler.TryCompile(Ignixa.FhirPath.Expressions.Expression)
       push      rbp
       sub       rsp,80
       lea       rbp,[rsp+80]
       xor       eax,eax
       mov       [rbp-58],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-50],ymm4
       vmovdqu   ymmword ptr [rbp-30],ymm4
       vmovdqa   xmmword ptr [rbp-10],xmm4
       mov       [rbp-60],rsp
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       rcx,[rbp+18]
       mov       rdx,227802CBB58
       call      qword ptr [7FF8AFB75080]; System.ArgumentNullException.ThrowIfNull(System.Object, System.String)
       nop
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.IdentifierExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-8],rax
       cmp       qword ptr [rbp-8],0
       jne       near ptr M21_L00
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.ScopeExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-10],rax
       cmp       qword ptr [rbp-10],0
       jne       near ptr M21_L01
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.ChildExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-18],rax
       cmp       qword ptr [rbp-18],0
       jne       near ptr M21_L02
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.PropertyAccessExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-20],rax
       cmp       qword ptr [rbp-20],0
       jne       near ptr M21_L03
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.ParenthesizedExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-28],rax
       cmp       qword ptr [rbp-28],0
       jne       near ptr M21_L04
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.BinaryExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-30],rax
       cmp       qword ptr [rbp-30],0
       jne       near ptr M21_L05
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.FunctionCallExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-38],rax
       cmp       qword ptr [rbp-38],0
       jne       near ptr M21_L06
       mov       rdx,[rbp+18]
       mov       rcx,offset MT_Ignixa.FhirPath.Expressions.ConstantExpression
       call      qword ptr [7FF8AF606688]; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       [rbp-40],rax
       cmp       qword ptr [rbp-40],0
       jne       near ptr M21_L07
       jmp       near ptr M21_L08
M21_L00:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-8]
       call      qword ptr [7FF8B0165CF8]
       mov       [rbp-48],rax
       jmp       near ptr M21_L09
M21_L01:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-10]
       call      qword ptr [7FF8B0165D10]
       mov       [rbp-48],rax
       jmp       near ptr M21_L09
M21_L02:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-18]
       call      qword ptr [7FF8B0165D28]; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler.CompileChild(Ignixa.FhirPath.Expressions.ChildExpression)
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L03:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-20]
       call      qword ptr [7FF8B0165D40]; Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler.CompilePropertyAccess(Ignixa.FhirPath.Expressions.PropertyAccessExpression)
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L04:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-28]
       call      qword ptr [7FF8B0165D58]
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L05:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-30]
       call      qword ptr [7FF8B0165D70]
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L06:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-38]
       call      qword ptr [7FF8B0165D88]
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L07:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-40]
       call      qword ptr [7FF8B0165DA0]
       mov       [rbp-48],rax
       jmp       short M21_L09
M21_L08:
       xor       eax,eax
       mov       [rbp-48],rax
M21_L09:
       mov       rax,[rbp-48]
       mov       [rbp-50],rax
M21_L10:
       mov       rax,[rbp-50]
       add       rsp,80
       pop       rbp
       ret
       push      rbp
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+80]
       mov       [rbp-58],rdx
       xor       eax,eax
       mov       [rbp-50],rax
       lea       rax,[M21_L10]
       add       rsp,30
       pop       rbp
       ret
; Total bytes of code 596
```
```assembly
; System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       xor       eax,eax
       mov       [rsp+20],rax
       mov       [rsp+28],rcx
       mov       rbx,rdx
       mov       rsi,r8
       mov       rdi,r9
       test      rbx,rbx
       je        short M22_L00
       call      qword ptr [7FF90B378018]
       lea       r8,[rsp+20]
       mov       rcx,rbx
       mov       r11,rax
       mov       rdx,rsi
       call      qword ptr [rax]
       test      eax,eax
       mov       rax,rdi
       cmovne    rax,[rsp+20]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M22_L00:
       mov       ecx,1
       call      qword ptr [7FF90B3866F8]
       int       3
; Total bytes of code 86
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,60
       lea       rbp,[rsp+90]
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-38],rdx
       mov       rsi,rcx
       mov       rdi,rdx
       mov       rbx,r8
       mov       r14,r9
       test      rbx,rbx
       je        near ptr M23_L05
       test      r14,r14
       je        near ptr M23_L04
       mov       r15,[rsi+8]
       mov       r13,[r15+8]
       cmp       byte ptr [rsi+15],0
       jne       short M23_L02
       mov       rcx,[rsi]
       call      qword ptr [7FF924A7FB30]
       mov       rcx,r13
       mov       r11,rax
       mov       rdx,rbx
       call      qword ptr [rax]
       mov       r13d,eax
M23_L00:
       mov       rcx,rdi
       call      qword ptr [7FF924A7FBA8]
       mov       rcx,rax
       lea       rdx,[rbp-40]
       mov       [rsp+20],rdx
       mov       rdx,r15
       mov       r8,rbx
       mov       r9d,r13d
       call      qword ptr [7FF924A807B8]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        short M23_L03
M23_L01:
       mov       rax,[rbp-40]
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M23_L02:
       mov       rcx,rbx
       lea       r11,[System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)]
       call      qword ptr [r11]
       mov       r13d,eax
       jmp       short M23_L00
M23_L03:
       mov       byte ptr [rbp-48],1
       mov       [rbp-44],r13d
       mov       rdx,rbx
       mov       r8,[rbp+30]
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-40]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-48]
       mov       r8,rbx
       mov       rdx,r15
       mov       rcx,rsi
       call      qword ptr [7FF924A807E0]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       short M23_L01
M23_L04:
       mov       rcx,[7FF924A80D78]
       mov       rcx,[rcx]
       call      qword ptr [7FF924A80208]
       int       3
M23_L05:
       mov       rcx,[7FF924A80C30]
       mov       rcx,[rcx]
       call      qword ptr [7FF924A80208]
       int       3
; Total bytes of code 284
```
**Extern method**
System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny_NoCacheLookup(Void*, System.Object)
System.Threading.Monitor.Exit(System.Object)

## .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3 (Job: ShortRun(IterationCount=10, LaunchCount=1, WarmupCount=5))

```assembly
; Ignixa.Benchmarks.FhirPathILBenchmarks.FirelySimplePath()
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+0A0]
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-48],rax
       mov       rbx,[rcx+18]
       mov       rcx,26D3D803090
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],0
       je        near ptr M00_L07
       call      qword ptr [7FF8AF965260]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].CreateValue()
       mov       rsi,rax
M00_L00:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rbx],rcx
       jne       near ptr M00_L14
       mov       r8,[rbx+40]
M00_L01:
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FF8B0176580]; Hl7.Fhir.ElementModel.TypedElementExtensions.ToPocoNode(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.ModelInspector, System.String)
       mov       rdi,rax
       mov       r14,[rsi+8]
       mov       r15,[r14+10]
       test      r15,r15
       je        near ptr M00_L16
       mov       r13,[r14+8]
       cmp       [r13],r13b
       mov       r12,[r13+8]
       mov       rcx,[r12+8]
       cmp       byte ptr [r13+15],0
       jne       near ptr M00_L08
       mov       r11,7FF8AF5732C0
       mov       rdx,26D00399E38
       call      qword ptr [r11]
       mov       esi,eax
M00_L02:
       lea       r9,[rbp-48]
       mov       [rsp+20],r9
       mov       r9d,esi
       mov       rdx,r12
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.String, Hl7.Fhir.Utility.CacheItem<Hl7.FhirPath.CompiledExpression>>
       mov       r8,26D00399E38
       call      qword ptr [7FF8AF9675D0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        near ptr M00_L15
M00_L03:
       mov       rbx,[rbp-48]
       xor       ecx,ecx
       mov       [rbp-48],rcx
       mov       rcx,r14
       call      qword ptr [7FF8B03057D0]; Hl7.Fhir.Utility.Cache`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].enforceMaxItems()
       cmp       [rbx],bl
       call      qword ptr [7FF8AFB16EF8]; System.DateTime.get_UtcNow()
       mov       rdx,rax
       lea       rcx,[rbp-58]
       mov       r8d,1
       call      qword ptr [7FF8B0395920]; System.DateTimeOffset.ToLocalTime(System.DateTime, Boolean)
       vmovups   xmm0,[rbp-58]
       vmovups   [rbx+10],xmm0
       mov       rbx,[rbx+8]
M00_L04:
       xor       ecx,ecx
       mov       [rbp-40],rcx
       mov       rcx,offset MT_Hl7.FhirPath.EvaluationContext
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,offset MT_System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       xor       edx,edx
       xor       r8d,r8d
       call      qword ptr [7FF8AF615908]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       lea       rcx,[r14+18]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Hl7.FhirPath.FhirPathCompiler+<>c__DisplayClass11_0.<Compile>b__0(Hl7.Fhir.Model.PocoNode, Hl7.FhirPath.EvaluationContext)
       cmp       [rbx+18],rcx
       jne       near ptr M00_L19
       mov       r15,[rbx+8]
       mov       rcx,rdi
       mov       rdx,r14
       call      qword ptr [7FF8B0396310]; Hl7.FhirPath.Expressions.Closure.Root(Hl7.Fhir.Model.PocoNodeOrList, Hl7.FhirPath.EvaluationContext)
       mov       r14,[r15+8]
       mov       rcx,offset Hl7.FhirPath.Expressions.EvaluatorVisitor+<>c__DisplayClass0_0.<WrapForDebugTracer>b__0(Hl7.FhirPath.Expressions.Closure, System.Collections.Generic.IEnumerable`1<Hl7.FhirPath.Expressions.Invokee>)
       cmp       [r14+18],rcx
       jne       near ptr M00_L18
       mov       rcx,[r14+8]
       mov       r8,26D3D8035C8
       mov       r8,[r8]
       mov       rdx,rax
       call      qword ptr [7FF8B0395518]; Hl7.FhirPath.Expressions.EvaluatorVisitor+<>c__DisplayClass0_0.<WrapForDebugTracer>b__0(Hl7.FhirPath.Expressions.Closure, System.Collections.Generic.IEnumerable`1<Hl7.FhirPath.Expressions.Invokee>)
       mov       rsi,rax
M00_L05:
       mov       rdx,rsi
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.ElementModel.ITypedElement>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M00_L09
       mov       rcx,offset MT_System.Linq.Enumerable+OrderedIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute, Hl7.Fhir.Specification.FhirRelease>
       cmp       [rax],rcx
       jne       near ptr M00_L20
       mov       rcx,rax
       call      qword ptr [7FF8AF9B7850]; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].ToArray()
M00_L06:
       nop
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L07:
       mov       rsi,[rcx+18]
       jmp       near ptr M00_L00
M00_L08:
       mov       edx,16
       mov       rcx,26D00399E44
       mov       r8d,0F4088D1E
       mov       r9d,20120C8C
       call      qword ptr [7FF8AFAB5F98]; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       mov       esi,eax
       jmp       near ptr M00_L02
M00_L09:
       mov       rdx,rsi
       mov       rcx,offset MT_System.Collections.Generic.ICollection<Hl7.Fhir.ElementModel.ITypedElement>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       r12,rax
       test      r12,r12
       je        short M00_L13
       mov       rcx,r12
       mov       r11,7FF8AF5732C8
       call      qword ptr [r11]
       test      eax,eax
       jne       short M00_L12
       test      byte ptr [7FF8B06F7588],1
       je        near ptr M00_L21
M00_L10:
       mov       rax,26D3D8047A0
       mov       rax,[rax]
M00_L11:
       jmp       near ptr M00_L06
M00_L12:
       movsxd    rdx,eax
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.ITypedElement[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rsi,rax
       mov       rcx,r12
       mov       rdx,rsi
       mov       r11,7FF8AF5732D0
       xor       r8d,r8d
       call      qword ptr [r11]
       mov       rax,rsi
       jmp       short M00_L11
M00_L13:
       mov       rdx,rsi
       mov       rcx,7FF8B03CEAE8
       call      qword ptr [7FF8B03970C0]; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       jmp       near ptr M00_L06
M00_L14:
       mov       rcx,rbx
       mov       r11,7FF8AF5732B8
       call      qword ptr [r11]
       mov       r8,rax
       jmp       near ptr M00_L01
M00_L15:
       mov       byte ptr [rbp-60],1
       mov       [rbp-5C],esi
       mov       rdx,26D00399E38
       mov       rcx,[r15+8]
       call      qword ptr [r15+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-48]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-60]
       mov       rdx,r12
       mov       rcx,r13
       mov       r8,26D00399E38
       call      qword ptr [7FF8AF96DEA8]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       near ptr M00_L03
M00_L16:
       mov       rcx,[r14+8]
       lea       r8,[rbp-40]
       mov       rdx,26D00399E38
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF99A4F0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValue(System.__Canon, System.__Canon ByRef)
       test      eax,eax
       jne       short M00_L17
       xor       ebx,ebx
       jmp       near ptr M00_L04
M00_L17:
       mov       rcx,[rbp-40]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B03057B8]; Hl7.Fhir.Utility.CacheItem`1[[System.__Canon, System.Private.CoreLib]].get_Value()
       mov       rbx,rax
       jmp       near ptr M00_L04
M00_L18:
       mov       r8,26D3D8035C8
       mov       r8,[r8]
       mov       rdx,rax
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       mov       rsi,rax
       jmp       near ptr M00_L05
M00_L19:
       mov       rdx,rdi
       mov       r8,r14
       mov       rcx,[rbx+8]
       call      qword ptr [rbx+18]
       mov       rsi,rax
       jmp       near ptr M00_L05
M00_L20:
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       jmp       near ptr M00_L06
M00_L21:
       mov       rcx,offset MT_System.Array+EmptyArray<Hl7.Fhir.ElementModel.ITypedElement>
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       jmp       near ptr M00_L10
; Total bytes of code 969
```
```assembly
; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].CreateValue()
M01_L00:
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
M01_L01:
       mov       rsi,[rbx+8]
       test      rsi,rsi
       je        short M01_L02
       mov       edi,[rsi+10]
       cmp       edi,8
       jne       short M01_L04
       mov       rcx,rbx
       mov       rdx,rsi
       xor       r8d,r8d
       call      qword ptr [7FF8AF965278]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ExecutionAndPublication(System.LazyHelper, Boolean)
M01_L02:
       cmp       qword ptr [rbx+8],0
       je        short M01_L03
       mov       rcx,rbx
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8AF965260]
M01_L03:
       mov       rax,[rbx+18]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M01_L04:
       cmp       edi,8
       ja        near ptr M01_L09
       mov       ecx,edi
       lea       rdx,[7FF8B05EDD28]
       mov       edx,[rdx+rcx*4]
       lea       r8,[M01_L01]
       add       rdx,r8
       jmp       rdx
       mov       rcx,rbx
       mov       rdx,rsi
       mov       r8d,1
       call      qword ptr [7FF8AF965278]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ExecutionAndPublication(System.LazyHelper, Boolean)
       jmp       short M01_L02
       mov       rcx,rbx
       call      qword ptr [7FF8B05065F8]
       jmp       short M01_L02
       mov       rcx,rbx
       mov       rdx,rsi
       call      qword ptr [7FF8B05065E0]
       jmp       short M01_L02
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+10]
       test      rax,rax
       je        short M01_L05
       mov       rcx,rax
       jmp       short M01_L06
M01_L05:
       mov       rdx,7FF8B0591F68
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
M01_L06:
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rdx,[rdx+18]
       test      rdx,rdx
       je        short M01_L07
       jmp       short M01_L08
M01_L07:
       mov       rdx,7FF8B0592008
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
M01_L08:
       mov       rcx,rdx
       call      qword ptr [7FF8B0506580]
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8B0506610]
       jmp       near ptr M01_L02
M01_L09:
       mov       rcx,[rsi+8]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0506628]
       int       3
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FF8AF965290]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ViaFactory(System.Threading.LazyThreadSafetyMode)
       jmp       near ptr M01_L02
       mov       rcx,rbx
       call      qword ptr [7FF8B0506640]
       jmp       near ptr M01_L02
; Total bytes of code 310
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementExtensions.ToPocoNode(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.ModelInspector, System.String)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0E8
       vzeroupper
       lea       rbp,[rsp+120]
       xor       eax,eax
       mov       [rbp-68],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       mov       [rbp-40],rax
       mov       rbx,rcx
       mov       rdi,rdx
       mov       rsi,r8
       lea       rcx,[rbp-0A0]
       call      CORINFO_HELP_INIT_PINVOKE_FRAME
       mov       r14,rax
       mov       rcx,rsp
       mov       [rbp-88],rcx
       mov       rcx,rbp
       mov       [rbp-78],rcx
       mov       rax,rbx
       test      rax,rax
       je        short M02_L00
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rax],rcx
       jne       near ptr M02_L24
       xor       eax,eax
M02_L00:
       test      rax,rax
       jne       near ptr M02_L45
       test      rdi,rdi
       jne       near ptr M02_L08
       mov       rcx,rbx
       test      rcx,rcx
       je        short M02_L01
       mov       r11,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rcx],r11
       jne       near ptr M02_L25
M02_L01:
       test      rcx,rcx
       je        near ptr M02_L29
       mov       r11,7FF8AF5732E0
       mov       rdx,26D00395DD0
       call      qword ptr [r11]
       mov       rdi,rax
       test      rdi,rdi
       je        near ptr M02_L28
       mov       rdx,rdi
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Object>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r15,rax
       test      r15,r15
       jne       near ptr M02_L17
       lea       r8,[rbp-40]
       mov       rdx,rdi
       mov       rcx,7FF8B0076700
       call      qword ptr [7FF8AFEAFE10]; System.Linq.Enumerable.TryGetFirstNonIterator[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
M02_L02:
       mov       rdi,rax
       test      rdi,rdi
       je        short M02_L03
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ModelInspector
       cmp       [rdi],rcx
       je        short M02_L03
       mov       rdx,rax
       call      qword ptr [7FF8AF824AF8]; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
       mov       rdi,rax
M02_L03:
       test      rdi,rdi
       jne       near ptr M02_L08
       mov       rcx,26D00395DD0
       call      System.RuntimeTypeHandle.GetAssembly(System.RuntimeType)
       mov       rdi,rax
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ModelInspector+<>c__DisplayClass2_0
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       [rbp-0C8],r13
       lea       rcx,[r13+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,26D3D801C68
       mov       rdi,[rcx]
       mov       [rbp-0D0],rdi
       mov       r12,[r13+8]
       mov       [rbp-0D8],r12
       mov       rcx,offset MT_System.Reflection.RuntimeAssembly
       cmp       [r12],rcx
       jne       near ptr M02_L34
       cmp       qword ptr [r12+10],0
       je        near ptr M02_L30
M02_L04:
       mov       r12,[r12+10]
       xor       ecx,ecx
       mov       [rbp-48],rcx
       mov       [rbp-50],rcx
M02_L05:
       test      r12,r12
       je        near ptr M02_L36
       mov       rcx,offset MT_System.Func<System.String, Hl7.Fhir.Introspection.ModelInspector>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       lea       rcx,[r14+8]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Hl7.Fhir.Introspection.ModelInspector+<>c__DisplayClass2_0.<ForAssembly>b__0(System.String)
       mov       [r14+18],rcx
       mov       r13,[rdi+8]
       mov       rcx,[r13+8]
       cmp       byte ptr [rdi+15],0
       jne       near ptr M02_L19
       mov       rdx,r12
       mov       r11,7FF8AF5732F8
       call      qword ptr [r11]
       mov       r15d,eax
M02_L06:
       lea       rdx,[rbp-68]
       mov       [rsp+20],rdx
       mov       rdx,r13
       mov       r8,r12
       mov       r9d,r15d
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.String, Hl7.Fhir.Introspection.ModelInspector>
       call      qword ptr [7FF8AF9675D0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        near ptr M02_L35
M02_L07:
       mov       rdi,[rbp-68]
       xor       ecx,ecx
       mov       [rbp-68],rcx
M02_L08:
       mov       rcx,offset MT_Hl7.Fhir.Serialization.PocoBuilderSettings
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       mov       word ptr [r12+10],101
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.NewPocoBuilder
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       lea       rcx,[r15+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+10]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       test      rbx,rbx
       je        near ptr M02_L44
       mov       rcx,r15
       mov       rdx,rbx
       xor       r8d,r8d
       xor       r9d,r9d
       call      qword ptr [7FF8B017DCF8]; Hl7.Fhir.ElementModel.NewPocoBuilder.classMappingForElement(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.PropertyMapping, System.Type)
       mov       r8,rax
       mov       rdx,rbx
       mov       rcx,r15
       call      qword ptr [7FF8B017DD10]; Hl7.Fhir.ElementModel.NewPocoBuilder.readFromElement(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.ClassMapping)
       mov       r15,rax
       mov       r12,r15
       test      r12,r12
       je        short M02_L09
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [r12],rcx
       jne       near ptr M02_L37
       xor       r12d,r12d
M02_L09:
       test      r12,r12
       jne       near ptr M02_L40
       test      r15,r15
       je        near ptr M02_L39
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       lea       rcx,[r12+10]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [r12+18],rcx
       mov       [r12+28],rcx
       mov       rdx,rsi
       test      rdx,rdx
       je        near ptr M02_L20
M02_L10:
       lea       rcx,[r12+8]
       call      CORINFO_HELP_ASSIGN_REF
M02_L11:
       test      rdi,rdi
       je        near ptr M02_L15
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [r12],rcx
       jne       near ptr M02_L43
       lea       r15,[r12+20]
       mov       rcx,26D3D803BE0
       mov       r8,[rcx]
       test      r8,r8
       je        near ptr M02_L41
M02_L12:
       mov       rbx,[r15]
       test      rbx,rbx
       je        near ptr M02_L22
M02_L13:
       cmp       [rbx],bl
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList+<>c__DisplayClass4_0
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       lea       rcx,[r15+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbx+8]
       cmp       qword ptr [rcx+8],0
       je        near ptr M02_L23
       call      qword ptr [7FF8AF965260]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].CreateValue()
       mov       rsi,rax
M02_L14:
       mov       rcx,offset MT_System.Collections.Generic.List<System.Object>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,[r15+8]
       call      System.Object.GetType()
       mov       r13,rax
       mov       rcx,offset MT_System.Object[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       lea       rcx,[r14+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8d,1
       mov       rdx,r14
       mov       rcx,7FF8B024E3E8
       call      qword ptr [7FF8AFEA6B80]; System.Runtime.InteropServices.CollectionsMarshal.SetCount[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.List`1<System.__Canon>, Int32)
       mov       ecx,[r14+10]
       mov       rdx,[r14+8]
       cmp       [rdx+8],ecx
       jb        near ptr M02_L42
       add       rdx,10
       mov       [rbp-0E0],rdx
       test      ecx,ecx
       je        near ptr M02_L46
       mov       rdx,[r15+8]
       mov       rcx,[rbp-0E0]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset MT_System.Func<System.Type, System.Collections.Generic.List<System.Object>, System.Collections.Generic.List<System.Object>>
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       lea       rcx,[rbx+8]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8B017AC40
       mov       [rbx+18],rcx
       mov       rcx,rsi
       mov       rdx,r13
       mov       r8,r14
       mov       r9,rbx
       cmp       [rcx],ecx
       call      qword ptr [7FF8B017EC88]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AddOrUpdate(System.__Canon, System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>)
M02_L15:
       mov       rax,r12
M02_L16:
       add       rsp,0E8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M02_L17:
       mov       rcx,offset MT_System.Linq.Enumerable+IListSkipTakeIterator<Newtonsoft.Json.Linq.JProperty>
       cmp       [r15],rcx
       jne       near ptr M02_L27
       mov       rcx,[r15+18]
       mov       r11,7FF8AF5732E8
       call      qword ptr [r11]
       cmp       eax,[r15+20]
       jg        near ptr M02_L26
       xor       ecx,ecx
       mov       [rbp-40],ecx
       xor       eax,eax
M02_L18:
       jmp       near ptr M02_L02
M02_L19:
       lea       rcx,[r12+0C]
       mov       edx,[r12+8]
       add       edx,edx
       mov       r8d,0F4088D1E
       mov       r9d,20120C8C
       call      qword ptr [7FF8AFAB5F98]; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       mov       r15d,eax
       jmp       near ptr M02_L06
M02_L20:
       mov       r8,offset MT_Hl7.Fhir.Model.Integer
       cmp       [r15],r8
       jne       near ptr M02_L38
       mov       rdx,26D002DD7C0
M02_L21:
       jmp       near ptr M02_L10
M02_L22:
       mov       rdx,r15
       mov       rcx,7FF8B024D7C0
       call      qword ptr [7FF8B017E310]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       mov       rbx,rax
       jmp       near ptr M02_L13
M02_L23:
       mov       rsi,[rcx+18]
       jmp       near ptr M02_L14
M02_L24:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       jmp       near ptr M02_L00
M02_L25:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Utility.IAnnotated
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M02_L01
M02_L26:
       mov       dword ptr [rbp-40],1
       mov       rcx,[r15+18]
       mov       edx,[r15+20]
       mov       r11,7FF8AF5732F0
       call      qword ptr [r11]
       jmp       near ptr M02_L18
M02_L27:
       lea       rdx,[rbp-40]
       mov       rcx,r15
       mov       rax,[r15]
       mov       rax,[rax+48]
       call      qword ptr [rax+10]
       jmp       near ptr M02_L18
M02_L28:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M02_L29:
       xor       edi,edi
       jmp       near ptr M02_L03
M02_L30:
       mov       [rbp+10],rbx
       mov       [rbp+20],rsi
       xor       ecx,ecx
       mov       [rbp-48],rcx
       mov       [rbp-50],r12
       vxorps    xmm0,xmm0,xmm0
       vmovups   [rbp-60],xmm0
       lea       rcx,[rbp-60]
       lea       rdx,[rbp-50]
       call      qword ptr [7FF8B050F8A0]
       mov       rcx,[rbp-60]
       mov       rdx,[rbp-58]
       mov       [rbp-0B8],rcx
       mov       [rbp-0B0],rdx
       lea       rcx,[rbp-0B8]
       lea       rdx,[rbp-48]
       mov       rax,7FF8AF83A140
       mov       [rbp-90],rax
       lea       rax,[M02_L31]
       mov       [rbp-80],rax
       lea       rax,[rbp-0A0]
       mov       [r14+8],rax
       mov       byte ptr [r14+4],0
       mov       rax,7FF90F155230
       call      rax
M02_L31:
       mov       byte ptr [r14+4],1
       cmp       dword ptr [7FF90F57C744],0
       je        short M02_L32
       call      qword ptr [7FF90F56A418]; CORINFO_HELP_STOP_FOR_GC
M02_L32:
       mov       rax,[rbp-98]
       mov       [r14+8],rax
       mov       r12,[rbp-0D8]
       lea       rcx,[r12+10]
       mov       rdx,[rbp-48]
       test      rcx,rcx
       jne       short M02_L33
       call      qword ptr [7FF8B05064C0]
       int       3
M02_L33:
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       rbx,[rbp+10]
       mov       rsi,[rbp+20]
       mov       rdi,[rbp-0D0]
       mov       r13,[rbp-0C8]
       jmp       near ptr M02_L04
M02_L34:
       mov       rcx,r12
       mov       rax,[r12]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       mov       r12,rax
       jmp       near ptr M02_L05
M02_L35:
       mov       byte ptr [rbp-0C0],1
       mov       [rbp-0BC],r15d
       mov       rdx,r12
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-68]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-0C0]
       mov       r8,r12
       mov       rdx,r13
       mov       rcx,rdi
       call      qword ptr [7FF8AF96DEA8]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       near ptr M02_L07
M02_L36:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       ecx,0C4A6
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rsi
       call      qword ptr [7FF8AF9666D0]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
M02_L37:
       mov       rdx,r15
       mov       rcx,offset MT_Hl7.Fhir.Model.PrimitiveType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r12,rax
       jmp       near ptr M02_L09
M02_L38:
       mov       rcx,r15
       mov       rax,[r15]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       rdx,rax
       jmp       near ptr M02_L21
M02_L39:
       xor       ecx,ecx
       call      qword ptr [7FF8B03056F8]
       int       3
M02_L40:
       mov       rcx,offset MT_Hl7.Fhir.Model.PrimitiveNode
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       byte ptr [rbp-0C0],0
       xor       r9d,r9d
       mov       [rbp-0BC],r9d
       mov       [rsp+20],rsi
       mov       r9,[rbp-0C0]
       mov       rdx,r12
       mov       rcx,rbx
       xor       r8d,r8d
       call      qword ptr [7FF8B017DA70]; Hl7.Fhir.Model.PrimitiveNode..ctor(Hl7.Fhir.Model.PrimitiveType, Hl7.Fhir.Model.PocoNodeOrList, System.Nullable`1<Int32>, System.String)
       mov       r12,rbx
       jmp       near ptr M02_L11
M02_L41:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Utility.AnnotationList>
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rdx,26D3D803BB8
       mov       rdx,[rdx]
       mov       rcx,rbx
       mov       r8,offset Hl7.Fhir.Model.PocoNode+<>c.<get_Annotations>b__58_0()
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D803BE0
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,rbx
       jmp       near ptr M02_L12
M02_L42:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M02_L43:
       mov       rcx,r12
       mov       rdx,rdi
       mov       r11,7FF8AF573300
       call      qword ptr [r11]
       jmp       near ptr M02_L15
M02_L44:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,7D2A
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF9666D0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M02_L45:
       jmp       near ptr M02_L16
M02_L46:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 2136
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rdi,r8
       mov       esi,r9d
       mov       rbp,[rdx+8]
       mov       rcx,[rdx+10]
       mov       eax,esi
       imul      rax,[rdx+28]
       shr       rax,20
       inc       rax
       mov       edx,[rcx+8]
       mov       r8d,edx
       imul      rax,r8
       shr       rax,20
       cmp       eax,edx
       jae       near ptr M03_L05
       mov       edx,eax
       mov       r14,[rcx+rdx*8+10]
       test      r14,r14
       je        short M03_L04
M03_L00:
       cmp       esi,[r14+20]
       jne       short M03_L03
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       r11,[rcx+0B8]
       test      r11,r11
       je        short M03_L02
M03_L01:
       mov       rdx,[r14+8]
       mov       rcx,rbp
       mov       r8,rdi
       call      qword ptr [r11]
       test      eax,eax
       je        short M03_L03
       mov       rdx,[r14+10]
       mov       rcx,[rsp+80]
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       eax,1
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M03_L02:
       mov       rcx,rbx
       mov       rdx,7FF8B0439B28
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M03_L01
M03_L03:
       mov       r14,[r14+18]
       test      r14,r14
       jne       short M03_L00
M03_L04:
       xor       eax,eax
       mov       rbx,[rsp+80]
       mov       [rbx],rax
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M03_L05:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 217
```
```assembly
; Hl7.Fhir.Utility.Cache`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].enforceMaxItems()
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+80]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   xmmword ptr [rbp-38],xmm4
       xor       eax,eax
       mov       [rbp-28],rax
       mov       [rbp-50],rsp
       mov       [rbp-20],rcx
       mov       [rbp+10],rcx
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rax,[rax+30]
       test      rax,rax
       je        short M04_L02
M04_L00:
       mov       rcx,rax
       mov       rax,[rbp+10]
       mov       rdx,[rax+8]
       call      qword ptr [7FF8B0396160]; System.Linq.Enumerable.Count[[System.Collections.Generic.KeyValuePair`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]], System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>)
       mov       rcx,[rbp+10]
       mov       rdx,[rcx+18]
       cmp       eax,[rdx+8]
       jg        near ptr M04_L06
M04_L01:
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M04_L02:
       mov       rcx,rdx
       mov       rdx,7FF8B03B0DA8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       short M04_L00
M04_L03:
       mov       rcx,[rbp-40]
       mov       r11,7FF8AF573228
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M04_L24
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       r11,[rax+58]
       test      r11,r11
       je        short M04_L04
       jmp       short M04_L05
M04_L04:
       mov       rcx,rdx
       mov       rdx,7FF8B03B3130
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M04_L05:
       lea       rdx,[rbp-30]
       mov       rcx,[rbp-40]
       call      qword ptr [r11]
       mov       rcx,[rbp+10]
       mov       rdx,[rcx+8]
       lea       r8,[rbp-38]
       mov       rcx,rdx
       mov       rdx,[rbp-30]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B03962B0]
       jmp       short M04_L03
M04_L06:
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rbx,[rax+38]
       test      rbx,rbx
       je        short M04_L07
       jmp       short M04_L08
M04_L07:
       mov       rcx,rdx
       mov       rdx,7FF8B03B1118
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rbx,rax
M04_L08:
       mov       rcx,[rbp+10]
       mov       rcx,[rcx+8]
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0396298]
       mov       rsi,rax
       mov       rcx,rbx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rbx,[rax+8]
       test      rbx,rbx
       jne       near ptr M04_L15
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rbx,[rax+38]
       test      rbx,rbx
       je        short M04_L09
       jmp       short M04_L10
M04_L09:
       mov       rcx,rdx
       mov       rdx,7FF8B03B1118
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rbx,rax
M04_L10:
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       cmp       qword ptr [rax+10],68
       jle       short M04_L11
       mov       rax,[rax+68]
       test      rax,rax
       je        short M04_L11
       jmp       short M04_L12
M04_L11:
       mov       rcx,rdx
       mov       rdx,7FF8B03B3488
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
M04_L12:
       mov       rcx,rax
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       rcx,rbx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rdx,[rax]
       mov       rcx,rdi
       mov       r8,7FF8B0392280
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rax,[rax+38]
       test      rax,rax
       je        short M04_L13
       jmp       short M04_L14
M04_L13:
       mov       rcx,rdx
       mov       rdx,7FF8B03B1118
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
M04_L14:
       mov       rcx,rax
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       lea       rcx,[rax+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rbx,rdi
M04_L15:
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rax,[rax+40]
       test      rax,rax
       je        short M04_L16
       jmp       short M04_L17
M04_L16:
       mov       rcx,rdx
       mov       rdx,7FF8B03B2F98
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
M04_L17:
       mov       rdx,[rax+18]
       mov       rdx,[rdx+18]
       test      rdx,rdx
       je        short M04_L18
       jmp       short M04_L19
M04_L18:
       mov       rcx,rax
       mov       rdx,7FF8B075E628
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rdx,rax
M04_L19:
       mov       rcx,rdx
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       dword ptr [rsp+20],1
       xor       ecx,ecx
       mov       [rsp+28],rcx
       mov       rcx,rdi
       mov       rdx,rsi
       mov       r8,rbx
       xor       r9d,r9d
       call      qword ptr [7FF8B050F7C8]
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rbx,[rax+48]
       test      rbx,rbx
       je        short M04_L20
       jmp       short M04_L21
M04_L20:
       mov       rcx,rdx
       mov       rdx,7FF8B03B30C8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rbx,rax
M04_L21:
       mov       rcx,[rbp+10]
       mov       rdx,[rcx]
       mov       rax,[rdx+30]
       mov       rax,[rax]
       mov       rsi,[rax+50]
       test      rsi,rsi
       je        short M04_L22
       jmp       short M04_L23
M04_L22:
       mov       rcx,rdx
       mov       rdx,7FF8B03B3100
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rsi,rax
M04_L23:
       mov       rcx,[rbp+10]
       mov       r8d,[rcx+28]
       mov       rcx,rbx
       mov       rdx,rdi
       call      qword ptr [7FF8B0396220]
       mov       rcx,rax
       mov       r11,rsi
       call      qword ptr [rsi]
       mov       [rbp-40],rax
       jmp       near ptr M04_L03
M04_L24:
       mov       rcx,[rbp-40]
       mov       r11,7FF8AF573230
       call      qword ptr [r11]
       mov       rcx,[rbp+10]
       jmp       near ptr M04_L01
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       mov       rbp,[rcx+30]
       mov       [rsp+30],rbp
       lea       rbp,[rbp+80]
       cmp       qword ptr [rbp-40],0
       je        short M04_L25
       mov       rcx,[rbp-40]
       mov       r11,7FF8AF573230
       call      qword ptr [r11]
M04_L25:
       nop
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
; Total bytes of code 857
```
```assembly
; System.DateTime.get_UtcNow()
       push      rbp
       push      rsi
       push      rbx
       sub       rsp,30
       lea       rbp,[rsp+40]
       lea       rcx,[rbp-18]
       mov       rax,7FFA3CBC7650
       call      rax
       mov       rbx,[rbp-18]
       mov       rax,26D3D801068
       mov       rsi,[rax]
       sub       rbx,[rsi+8]
       cmp       dword ptr [7FF90F57C744],0
       jne       short M05_L01
M05_L00:
       mov       eax,0B2D05E00
       cmp       rbx,rax
       jae       short M05_L02
       mov       rax,rbx
       add       rax,[rsi+10]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rbp
       ret
M05_L01:
       call      CORINFO_HELP_POLL_GC
       jmp       short M05_L00
M05_L02:
       call      qword ptr [7FF8AFB17120]; System.DateTime.UpdateLeapSecondCacheAndReturnUtcNow()
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rbp
       ret
; Total bytes of code 105
```
```assembly
; System.DateTimeOffset.ToLocalTime(System.DateTime, Boolean)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,rdx
       mov       edi,r8d
       mov       rcx,26D3D804278
       mov       rbp,[rcx]
       mov       rcx,[rbp+8]
       test      rcx,rcx
       je        near ptr M06_L02
M06_L00:
       mov       rdx,rsi
       mov       r9,rbp
       mov       r8d,2
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0395FC8]; System.TimeZoneInfo.GetUtcOffset(System.DateTime, System.TimeZoneInfoOptions, CachedData)
       mov       rcx,rax
       mov       r8,3FFFFFFFFFFFFFFF
       and       r8,rsi
       add       r8,rcx
       mov       rax,2BCA2875F4373FFF
       cmp       r8,rax
       ja        near ptr M06_L03
M06_L01:
       mov       rdx,1CA213D840BAF7D5
       mov       rax,rdx
       imul      rcx
       mov       rax,rdx
       shr       rax,3F
       sar       rdx,1A
       add       rax,rdx
       imul      rdx,rax,23C34600
       mov       r10,rcx
       sub       r10,rdx
       jne       near ptr M06_L07
       mov       rdx,0FFFFFF8AA7425000
       cmp       rcx,rdx
       jl        near ptr M06_L06
       mov       rdx,7558BDB000
       cmp       rcx,rdx
       jg        near ptr M06_L06
       cwde
       mov       rdx,2BCA2875F4373FFF
       cmp       r8,rdx
       ja        near ptr M06_L05
       mov       rdx,3FFFFFFFFFFFFFFF
       and       r8,rdx
       sub       r8,rcx
       mov       rcx,2BCA2875F4373FFF
       cmp       r8,rcx
       ja        short M06_L04
       mov       rcx,2BCA2875F4373FFF
       cmp       r8,rcx
       ja        near ptr M06_L05
       mov       [rbx],ax
       mov       [rbx+8],r8
       mov       rax,rbx
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M06_L02:
       mov       rcx,rbp
       call      qword ptr [7FF8B0395AE8]; System.TimeZoneInfo+CachedData.CreateLocal()
       mov       rcx,rax
       jmp       near ptr M06_L00
M06_L03:
       test      dil,dil
       jne       near ptr M06_L08
       xor       eax,eax
       mov       rdx,2BCA2875F4373FFF
       test      r8,r8
       cmovge    rax,rdx
       mov       r8,rax
       jmp       near ptr M06_L01
M06_L04:
       mov       rcx,offset MT_System.ArgumentOutOfRangeException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,1E75
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rsi,rax
       call      qword ptr [7FF8B0507A68]
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AFB16FD0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M06_L05:
       call      qword ptr [7FF8B05073A8]
       int       3
M06_L06:
       mov       rcx,offset MT_System.ArgumentOutOfRangeException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,1E75
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rsi,rax
       call      qword ptr [7FF8B0507A38]
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AFB16FD0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M06_L07:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B0507A50]
       mov       rsi,rax
       mov       ecx,1E75
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AF966F28]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M06_L08:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B050D218]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF965608]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 595
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rsi,r8
       test      edx,edx
       jl        near ptr M07_L12
       test      edx,edx
       jg        near ptr M07_L06
M07_L00:
       mov       rdx,rsi
       test      rdx,rdx
       jne       short M07_L02
       mov       rcx,[rbx]
       mov       rax,[rcx+30]
       mov       rax,[rax]
       mov       rax,[rax+88]
       test      rax,rax
       je        near ptr M07_L07
M07_L01:
       mov       rcx,rax
       call      qword ptr [7FF8AF615AD0]; System.Collections.Generic.EqualityComparer`1[[System.__Canon, System.Private.CoreLib]].get_Default()
       mov       rdx,rax
M07_L02:
       lea       rcx,[rbx+18]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbx]
       mov       rcx,[rcx+30]
       mov       rcx,[rcx]
       mov       rdx,offset MT_System.String
       cmp       [rcx],rdx
       jne       near ptr M07_L09
       mov       rsi,[rbx+18]
       mov       rcx,26D3D800048
       cmp       rsi,[rcx]
       jne       near ptr M07_L10
       mov       rcx,26D3D800050
       mov       rdi,[rcx]
M07_L03:
       test      rdi,rdi
       je        short M07_L09
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+80]
       test      rax,rax
       je        short M07_L08
       mov       rcx,rax
M07_L04:
       mov       rdx,rdi
       cmp       [rdx],rcx
       je        short M07_L05
       mov       rdx,rdi
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       rdx,rax
M07_L05:
       lea       rcx,[rbx+18]
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M07_L06:
       mov       rcx,rbx
       call      qword ptr [7FF8AF615920]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Initialize(Int32)
       jmp       near ptr M07_L00
M07_L07:
       mov       rdx,7FF8B043A440
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       near ptr M07_L01
M07_L08:
       mov       rdx,7FF8B043A430
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       short M07_L04
M07_L09:
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M07_L10:
       mov       rcx,26D3D800068
       cmp       rsi,[rcx]
       je        short M07_L11
       mov       rcx,26D3D800070
       xor       edi,edi
       mov       rax,26D3D800060
       cmp       rsi,[rcx]
       cmove     rdi,[rax]
       jmp       near ptr M07_L03
M07_L11:
       mov       rcx,26D3D800058
       mov       rdi,[rcx]
       jmp       near ptr M07_L03
M07_L12:
       mov       ecx,16
       call      qword ptr [7FF8AF61F168]
       int       3
; Total bytes of code 362
```
```assembly
; Hl7.FhirPath.FhirPathCompiler+<>c__DisplayClass11_0.<Compile>b__0(Hl7.Fhir.Model.PocoNode, Hl7.FhirPath.EvaluationContext)
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rcx,rdx
       mov       rdx,r8
       call      qword ptr [7FF8B0396310]; Hl7.FhirPath.Expressions.Closure.Root(Hl7.Fhir.Model.PocoNodeOrList, Hl7.FhirPath.EvaluationContext)
       mov       r10,[rbx+8]
       mov       rcx,offset Hl7.FhirPath.Expressions.EvaluatorVisitor+<>c__DisplayClass0_0.<WrapForDebugTracer>b__0(Hl7.FhirPath.Expressions.Closure, System.Collections.Generic.IEnumerable`1<Hl7.FhirPath.Expressions.Invokee>)
       cmp       [r10+18],rcx
       jne       short M08_L00
       mov       rcx,[r10+8]
       mov       r8,26D3D8035C8
       mov       r8,[r8]
       mov       rdx,rax
       add       rsp,20
       pop       rbx
       jmp       qword ptr [7FF8B0395518]; Hl7.FhirPath.Expressions.EvaluatorVisitor+<>c__DisplayClass0_0.<WrapForDebugTracer>b__0(Hl7.FhirPath.Expressions.Closure, System.Collections.Generic.IEnumerable`1<Hl7.FhirPath.Expressions.Invokee>)
M08_L00:
       mov       r8,26D3D8035C8
       mov       r8,[r8]
       mov       rdx,rax
       mov       rcx,[r10+8]
       add       rsp,20
       pop       rbx
       jmp       qword ptr [r10+18]
; Total bytes of code 100
```
```assembly
; Hl7.FhirPath.Expressions.Closure.Root(Hl7.Fhir.Model.PocoNodeOrList, Hl7.FhirPath.EvaluationContext)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,48
       lea       rbp,[rsp+80]
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rbp-50],xmm4
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-60],rsp
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,rsi
       test      rdi,rdi
       je        near ptr M09_L16
M09_L00:
       cmp       qword ptr [rdi+10],0
       jne       short M09_L01
       mov       rcx,rbx
       call      qword ptr [7FF8B0396388]; Hl7.Fhir.Model.PocoNodeExtensions.GetResourceContext(Hl7.Fhir.Model.PocoNodeOrList)
       lea       rcx,[rdi+10]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M09_L01:
       cmp       qword ptr [rdi+8],0
       jne       short M09_L03
       mov       rcx,rbx
       call      qword ptr [7FF8B0396388]; Hl7.Fhir.Model.PocoNodeExtensions.GetResourceContext(Hl7.Fhir.Model.PocoNodeOrList)
       mov       rdx,rax
       test      rdx,rdx
       je        near ptr M09_L18
       mov       rcx,[rdx+8]
       test      rcx,rcx
       je        short M09_L02
       cmp       dword ptr [rcx+8],9
       jne       short M09_L02
       vmovups   xmm0,[rcx+0C]
       vpxor     xmm0,xmm0,[7FF8B07623D0]
       vmovups   xmm1,[rcx+0E]
       vpxor     xmm1,xmm1,[7FF8B07623E0]
       vpor      xmm0,xmm1,xmm0
       vptest    xmm0,xmm0
       sete      cl
       movzx     ecx,cl
       test      ecx,ecx
       jne       near ptr M09_L17
M09_L02:
       lea       rcx,[rdi+8]
       call      CORINFO_HELP_ASSIGN_REF
M09_L03:
       test      rsi,rsi
       je        near ptr M09_L19
M09_L04:
       mov       rcx,offset MT_Hl7.FhirPath.Expressions.Closure
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,offset MT_System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,r15
       xor       edx,edx
       xor       r8d,r8d
       call      qword ptr [7FF8AF615908]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       lea       rcx,[r14+18]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r14+10]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       ecx,[rsi+30]
       lea       edx,[rcx+1]
       mov       [rsi+30],edx
       mov       [r14+28],ecx
       cmp       qword ptr [rsi+28],0
       setne     cl
       mov       [r14+2C],cl
       mov       rcx,[r14+10]
       mov       r15,[rcx+18]
       mov       rcx,offset MT_System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>
       cmp       [r15],rcx
       jne       near ptr M09_L21
       mov       ecx,[r15+38]
       sub       ecx,[r15+40]
       jne       near ptr M09_L20
       mov       rcx,26D3D8042B8
       mov       rcx,[rcx]
M09_L05:
       mov       [rbp-58],rcx
M09_L06:
       mov       rdx,offset MT_System.GenericEmptyEnumerator<System.Collections.Generic.KeyValuePair<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>>
       cmp       [rcx],rdx
       je        short M09_L07
       mov       r11,7FF8AF573258
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M09_L22
       lea       rdx,[rbp-48]
       mov       rcx,[rbp-58]
       mov       r11,7FF8AF573260
       call      qword ptr [r11]
       mov       rcx,r14
       mov       rdx,[rbp-48]
       mov       r8,[rbp-40]
       call      qword ptr [7FF8B03C5D90]; Hl7.FhirPath.Expressions.Closure.SetValue(System.String, System.Collections.Generic.IEnumerable`1<Hl7.Fhir.Model.PocoNode>)
       mov       rcx,[rbp-58]
       jmp       short M09_L06
M09_L07:
       mov       rcx,[r14+18]
       mov       rdx,26D003A80F8
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,rbx
       mov       rdx,26D003A80F8
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       mov       rcx,[r14+18]
       mov       rdx,26D003A8128
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,rbx
       mov       rdx,26D003A8128
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       mov       rcx,offset MT_System.Int32
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       dword ptr [r13+8],1
       mov       rcx,26D002DD7E8
       mov       rax,offset MT_Hl7.Fhir.Model.Integer
       mov       eax,[rax]
       and       eax,0C0000
       cmp       eax,40000
       sete      al
       movzx     eax,al
       test      eax,eax
       jne       near ptr M09_L15
       call      qword ptr [7FF8AF96F750]; System.RuntimeType.CreateInstanceOfT()
       mov       r12,rax
M09_L08:
       xor       ecx,ecx
       mov       [rbp-50],rcx
       mov       rcx,offset MT_Hl7.Fhir.Model.Integer
       cmp       [r12],rcx
       jne       near ptr M09_L23
       lea       rcx,[r12+30]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
M09_L09:
       mov       rcx,offset MT_Hl7.Fhir.Model.PrimitiveNode
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       lea       rcx,[r13+30]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r13+10]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [r13+18],rcx
       mov       [r13+28],rcx
       mov       rcx,offset MT_Hl7.Fhir.Model.Integer
       cmp       [r12],rcx
       jne       near ptr M09_L24
       mov       rdx,26D002DD7C0
M09_L10:
       lea       rcx,[r13+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[r14+18]
       mov       rdx,26D003A8188
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,r13
       mov       rdx,26D003A8188
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       mov       rcx,[r14+18]
       mov       rdx,26D002E0780
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,rbx
       mov       rdx,26D002E0780
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       mov       rcx,[rdi+10]
       test      rcx,rcx
       je        short M09_L12
       mov       rdx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [rcx],rdx
       jne       near ptr M09_L25
       xor       eax,eax
M09_L11:
       movzx     ecx,al
       test      ecx,ecx
       jne       short M09_L12
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rsi,rax
       mov       rdx,[rdi+10]
       lea       rcx,[rsi+10]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[r14+18]
       mov       rdx,26D002F55B8
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,rsi
       mov       rdx,26D002F55B8
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
M09_L12:
       mov       rcx,[rdi+8]
       test      rcx,rcx
       je        short M09_L14
       mov       rdx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [rcx],rdx
       jne       near ptr M09_L26
       xor       esi,esi
M09_L13:
       movzx     ecx,sil
       test      ecx,ecx
       jne       short M09_L14
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       r15,rax
       mov       rdx,[rdi+8]
       lea       rcx,[r15+10]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[r14+18]
       mov       rdx,26D003A81B8
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[r14+18]
       cmp       [rcx],cl
       mov       r8,r15
       mov       rdx,26D003A81B8
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
M09_L14:
       mov       rax,r14
       add       rsp,48
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M09_L15:
       xor       edx,edx
       mov       [rbp-50],rdx
       lea       rdx,[rbp-50]
       call      qword ptr [7FF8B050D350]
       mov       r12,[rbp-50]
       jmp       near ptr M09_L08
M09_L16:
       mov       rcx,offset MT_Hl7.FhirPath.EvaluationContext
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       rcx,rdi
       call      qword ptr [7FF8B0305788]; Hl7.FhirPath.EvaluationContext..ctor()
       jmp       near ptr M09_L00
M09_L17:
       mov       rcx,rdx
       mov       rax,[rdx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       rdx,rax
       jmp       near ptr M09_L02
M09_L18:
       xor       edx,edx
       jmp       near ptr M09_L02
M09_L19:
       mov       rcx,offset MT_Hl7.FhirPath.EvaluationContext
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       call      qword ptr [7FF8B0305788]; Hl7.FhirPath.EvaluationContext..ctor()
       jmp       near ptr M09_L04
M09_L20:
       mov       rcx,offset MT_System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>+Enumerator
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       lea       rdx,[r12+8]
       mov       rcx,r15
       call      qword ptr [7FF8AF967108]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetEnumerator()
       mov       rcx,r12
       jmp       near ptr M09_L05
M09_L21:
       mov       rcx,r15
       mov       r11,7FF8AF573250
       call      qword ptr [r11]
       mov       rcx,rax
       jmp       near ptr M09_L05
M09_L22:
       mov       rcx,[rbp-58]
       mov       r11,7FF8AF573268
       call      qword ptr [r11]
       jmp       near ptr M09_L07
M09_L23:
       mov       rcx,r12
       mov       rdx,r13
       mov       rax,[r12]
       mov       rax,[rax+50]
       call      qword ptr [rax+28]
       jmp       near ptr M09_L09
M09_L24:
       mov       rcx,r12
       mov       rax,[r12]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       rdx,rax
       jmp       near ptr M09_L10
M09_L25:
       xor       edx,edx
       mov       rax,[rcx]
       mov       rax,[rax+58]
       call      qword ptr [rax+28]
       jmp       near ptr M09_L11
M09_L26:
       xor       edx,edx
       mov       rax,[rcx]
       mov       rax,[rax+58]
       call      qword ptr [rax+28]
       mov       esi,eax
       jmp       near ptr M09_L13
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+80]
       cmp       qword ptr [rbp-58],0
       je        short M09_L27
       mov       rcx,offset MT_System.GenericEmptyEnumerator<System.Collections.Generic.KeyValuePair<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>>
       mov       r11,[rbp-58]
       cmp       [r11],rcx
       je        short M09_L27
       mov       rcx,r11
       mov       r11,7FF8AF573268
       call      qword ptr [r11]
M09_L27:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 1507
```
```assembly
; Hl7.FhirPath.Expressions.EvaluatorVisitor+<>c__DisplayClass0_0.<WrapForDebugTracer>b__0(Hl7.FhirPath.Expressions.Closure, System.Collections.Generic.IEnumerable`1<Hl7.FhirPath.Expressions.Invokee>)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+0A0]
       xor       eax,eax
       mov       [rbp-40],rax
       mov       rsi,rcx
       mov       rbx,rdx
       cmp       byte ptr [rbx+2C],0
       jne       short M10_L03
       mov       rdx,26D3D8042D0
       mov       rdi,[rdx]
M10_L00:
       mov       rax,[rsi+8]
       mov       rdx,rbx
       mov       rcx,[rax+8]
       call      qword ptr [rax+18]
       mov       r14,rax
       mov       rax,[rbx+10]
       mov       r15,[rax+28]
       test      r15,r15
       jne       short M10_L04
M10_L01:
       cmp       byte ptr [rbx+2C],0
       jne       near ptr M10_L07
M10_L02:
       mov       rax,r14
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M10_L03:
       mov       rdi,[rbx+8]
       jmp       short M10_L00
M10_L04:
       mov       rsi,[rsi+10]
       mov       r13d,[rbx+28]
       mov       rcx,rbx
       call      qword ptr [7FF8B0396808]; Hl7.FhirPath.Expressions.Closure.get_focus()
       mov       r12,rax
       mov       rcx,rbx
       mov       rdx,26D003A80F8
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       [rbp-48],rax
       mov       rcx,rbx
       mov       rdx,26D003A8188
       mov       r8,[rbx]
       mov       r8,[r8+40]
       call      qword ptr [r8+30]
       test      rax,rax
       jne       short M10_L05
       xor       r10d,r10d
       mov       [rbp-50],r10
       jmp       short M10_L06
M10_L05:
       lea       r8,[rbp-40]
       mov       rdx,rax
       mov       rcx,7FF8B03CBE50
       call      qword ptr [7FF8AF8252F0]; System.Linq.Enumerable.TryGetFirst[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
       mov       [rbp-50],rax
M10_L06:
       mov       rcx,rbx
       mov       rdx,26D003A8158
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       [rsp+38],r14
       mov       rdx,[rbx+18]
       mov       [rsp+40],rdx
       mov       [rsp+30],rax
       mov       rdx,rsi
       mov       r8d,r13d
       mov       r9,r12
       mov       rsi,[rbp-48]
       mov       [rsp+20],rsi
       mov       rsi,[rbp-50]
       mov       [rsp+28],rsi
       mov       rcx,r15
       mov       r11,7FF8AF573040
       call      qword ptr [r11]
       jmp       near ptr M10_L01
M10_L07:
       lea       rcx,[rbx+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M10_L02
; Total bytes of code 340
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M11_L02
       cmp       [rdx],rcx
       je        short M11_L02
       mov       rax,[rdx]
       mov       r8,[rax+10]
M11_L00:
       cmp       r8,rcx
       je        short M11_L02
       test      r8,r8
       je        short M11_L01
       mov       r8,[r8+10]
       cmp       r8,rcx
       je        short M11_L02
       test      r8,r8
       je        short M11_L01
       mov       r8,[r8+10]
       cmp       r8,rcx
       je        short M11_L02
       test      r8,r8
       jne       short M11_L03
M11_L01:
       xor       edx,edx
M11_L02:
       mov       rax,rdx
       ret
M11_L03:
       mov       r8,[r8+10]
       cmp       r8,rcx
       je        short M11_L02
       test      r8,r8
       je        short M11_L01
       mov       r8,[r8+10]
       jmp       short M11_L00
; Total bytes of code 81
```
```assembly
; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].ToArray()
M12_L00:
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rsp+20],ymm4
       mov       [rsp+40],rcx
       mov       rbx,rcx
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx+8]
       mov       rsi,[rdx+40]
       test      rsi,rsi
       je        near ptr M12_L05
M12_L01:
       mov       rdi,[rbx+18]
       mov       rcx,[rsi+18]
       cmp       qword ptr [rcx+8],30
       jle       near ptr M12_L06
       mov       rcx,[rcx+30]
       test      rcx,rcx
       je        near ptr M12_L06
M12_L02:
       mov       rdx,rdi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rbp,rax
       test      rbp,rbp
       je        near ptr M12_L09
       mov       rsi,offset MT_System.Linq.Enumerable+OrderedIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute, Hl7.Fhir.Specification.FhirRelease>
       cmp       [rbp],rsi
       jne       near ptr M12_L19
       mov       rdi,[rbp+18]
       mov       rdx,rdi
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M12_L07
       cmp       [rax],rsi
       jne       near ptr M12_L17
       mov       rcx,rax
       call      qword ptr [7FF8AF9B7850]
       mov       rsi,rax
M12_L03:
       cmp       dword ptr [rsi+8],1
       jg        near ptr M12_L18
M12_L04:
       cmp       dword ptr [rsi+8],1
       jg        near ptr M12_L20
       mov       rax,rsi
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M12_L05:
       mov       rdx,7FF8B06A3BB0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rsi,rax
       jmp       near ptr M12_L01
M12_L06:
       mov       rcx,rsi
       mov       rdx,7FF8B05902A8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M12_L02
M12_L07:
       mov       rdx,rdi
       mov       rcx,offset MT_System.Collections.Generic.ICollection<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        short M12_L08
       mov       rdx,rax
       mov       rcx,7FF8AFEC8FE8
       call      qword ptr [7FF8AF96D680]; System.Linq.Enumerable.ICollectionToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.ICollection`1<System.__Canon>)
       mov       rsi,rax
       jmp       short M12_L03
M12_L08:
       mov       rdx,rdi
       mov       rcx,7FF8B06F6148
       call      qword ptr [7FF8B03970C0]; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       mov       rsi,rax
       jmp       near ptr M12_L03
M12_L09:
       mov       rcx,[rsi+18]
       cmp       qword ptr [rcx+8],38
       jle       short M12_L12
       mov       rcx,[rcx+38]
       test      rcx,rcx
       je        short M12_L12
M12_L10:
       mov       rdx,rdi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rbp,rax
       test      rbp,rbp
       je        short M12_L14
       mov       rcx,[rsi+18]
       cmp       qword ptr [rcx+8],48
       jle       short M12_L13
       mov       rcx,[rcx+48]
       test      rcx,rcx
       je        short M12_L13
M12_L11:
       mov       rdx,rbp
       call      qword ptr [7FF8AF96D680]; System.Linq.Enumerable.ICollectionToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.ICollection`1<System.__Canon>)
       mov       rsi,rax
       jmp       near ptr M12_L04
M12_L12:
       mov       rcx,rsi
       mov       rdx,7FF8B05902B8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M12_L10
M12_L13:
       mov       rcx,rsi
       mov       rdx,7FF8B0590318
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M12_L11
M12_L14:
       mov       rcx,[rsi+18]
       cmp       qword ptr [rcx+8],40
       jle       short M12_L16
       mov       rcx,[rcx+40]
       test      rcx,rcx
       je        short M12_L16
M12_L15:
       mov       rdx,rdi
       call      qword ptr [7FF8B03970C0]; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       mov       rsi,rax
       jmp       near ptr M12_L04
M12_L16:
       mov       rcx,rsi
       mov       rdx,7FF8B05902E0
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M12_L15
M12_L17:
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       rsi,rax
       jmp       near ptr M12_L03
M12_L18:
       mov       edx,[rsi+8]
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ValidatingFhirModelAttribute[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rdi,rax
       lea       r8,[rdi+10]
       mov       edx,[rdi+8]
       mov       [rsp+30],r8
       mov       [rsp+38],edx
       lea       r8,[rsp+30]
       mov       rdx,rsi
       mov       rcx,rbp
       call      qword ptr [7FF8AF96D740]; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].Fill(System.__Canon[], System.Span`1<System.__Canon>)
       mov       rsi,rdi
       jmp       near ptr M12_L04
M12_L19:
       mov       rcx,rbp
       mov       rax,[rbp]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       rsi,rax
       jmp       near ptr M12_L04
M12_L20:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx+8]
       mov       rax,[rdx+48]
       test      rax,rax
       je        short M12_L21
       mov       rcx,rax
       jmp       short M12_L22
M12_L21:
       mov       rdx,7FF8B06A3BD0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
M12_L22:
       mov       edx,[rsi+8]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rdi,rax
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx+8]
       mov       rdx,[rdx+50]
       test      rdx,rdx
       je        short M12_L23
       jmp       short M12_L24
M12_L23:
       mov       rdx,7FF8B06A3C48
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
M12_L24:
       mov       rcx,[rdx+30]
       mov       rcx,[rcx]
       mov       rax,[rcx+30]
       test      rax,rax
       je        short M12_L25
       jmp       short M12_L26
M12_L25:
       mov       rcx,rdx
       mov       rdx,7FF8B065ABD8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
M12_L26:
       cmp       [rdi],rax
       jne       short M12_L27
       lea       r8,[rdi+10]
       mov       edx,[rdi+8]
       mov       [rsp+20],r8
       mov       [rsp+28],edx
       lea       r8,[rsp+20]
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AF96D740]; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].Fill(System.__Canon[], System.Span`1<System.__Canon>)
       mov       rax,rdi
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M12_L27:
       call      qword ptr [7FF8B050C0A8]
       int       3
; Total bytes of code 796
```
```assembly
; System.Marvin.ComputeHash32(Byte ByRef, UInt32, UInt32, UInt32)
       cmp       edx,8
       jb        short M13_L01
       mov       eax,edx
       shr       eax,3
M13_L00:
       add       r8d,[rcx]
       mov       r10d,[rcx+4]
       xor       r9d,r8d
       rol       r8d,14
       add       r8d,r9d
       rol       r9d,9
       xor       r9d,r8d
       rol       r8d,1B
       add       r8d,r9d
       rol       r9d,13
       add       r10d,r8d
       mov       r8d,r9d
       xor       r8d,r10d
       rol       r10d,14
       add       r10d,r8d
       rol       r8d,9
       xor       r8d,r10d
       rol       r10d,1B
       add       r10d,r8d
       rol       r8d,13
       mov       r9d,r8d
       add       rcx,8
       dec       eax
       mov       r8d,r10d
       jne       short M13_L00
       test      dl,4
       je        short M13_L03
       jmp       short M13_L02
M13_L01:
       cmp       edx,4
       jb        short M13_L05
M13_L02:
       add       r8d,[rcx]
       xor       r9d,r8d
       rol       r8d,14
       add       r8d,r9d
       rol       r9d,9
       xor       r9d,r8d
       rol       r8d,1B
       add       r8d,r9d
       rol       r9d,13
M13_L03:
       mov       eax,edx
       and       rax,7
       mov       eax,[rcx+rax-4]
       shr       eax,8
       or        eax,80000000
       not       edx
       shl       edx,3
       shrx      edx,eax,edx
M13_L04:
       add       edx,r8d
       mov       eax,r9d
       xor       eax,edx
       rol       edx,14
       add       edx,eax
       rol       eax,9
       xor       eax,edx
       rol       edx,1B
       add       edx,eax
       rol       eax,13
       xor       eax,edx
       mov       ecx,edx
       rol       ecx,14
       add       ecx,eax
       rol       eax,9
       xor       eax,ecx
       rol       ecx,1B
       add       ecx,eax
       rol       eax,13
       xor       eax,ecx
       ret
M13_L05:
       mov       eax,80
       test      dl,1
       je        short M13_L06
       mov       eax,edx
       and       rax,2
       movzx     eax,byte ptr [rcx+rax]
       or        eax,8000
M13_L06:
       test      dl,2
       mov       edx,eax
       je        short M13_L04
       shl       edx,10
       movzx     eax,word ptr [rcx]
       or        edx,eax
       jmp       short M13_L04
; Total bytes of code 257
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rdx,rdx
       je        short M14_L03
       mov       rax,[rdx]
       movzx     r8d,word ptr [rax+0E]
       test      r8,r8
       je        short M14_L02
       mov       r10,[rax+38]
       cmp       r8,4
       jl        short M14_L01
M14_L00:
       cmp       [r10],rcx
       je        short M14_L03
       cmp       [r10+8],rcx
       je        short M14_L03
       cmp       [r10+10],rcx
       je        short M14_L03
       cmp       [r10+18],rcx
       je        short M14_L03
       add       r10,20
       add       r8,0FFFFFFFFFFFFFFFC
       cmp       r8,4
       jge       short M14_L00
       test      r8,r8
       je        short M14_L02
M14_L01:
       cmp       [r10],rcx
       je        short M14_L03
       add       r10,8
       dec       r8
       test      r8,r8
       jg        short M14_L01
M14_L02:
       test      dword ptr [rax],504C0000
       jne       short M14_L04
       xor       edx,edx
M14_L03:
       mov       rax,rdx
       ret
M14_L04:
       jmp       qword ptr [7FF8AF96F0A8]; System.Runtime.CompilerServices.CastHelpers.IsInstance_Helper(Void*, System.Object)
; Total bytes of code 107
```
```assembly
; System.Linq.Enumerable.<ToArray>g__EnumerableToArray|314_0[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,188
       xor       eax,eax
       mov       [rsp+28],rax
       vxorps    xmm4,xmm4,xmm4
       mov       rax,0FFFFFFFFFFFFFEB0
M15_L00:
       vmovdqa   xmmword ptr [rsp+rax+180],xmm4
       vmovdqa   xmmword ptr [rsp+rax+190],xmm4
       vmovdqa   xmmword ptr [rsp+rax+1A0],xmm4
       add       rax,30
       jne       short M15_L00
       mov       [rsp+180],rcx
       mov       rbx,rdx
       test      rbx,rbx
       je        near ptr M15_L29
       vxorps    ymm0,ymm0,ymm0
       vmovdqu   ymmword ptr [rsp+140],ymm0
       vmovdqu   ymmword ptr [rsp+160],ymm0
       vxorps    ymm0,ymm0,ymm0
       vmovdqu   ymmword ptr [rsp+48],ymm0
       vmovdqu   ymmword ptr [rsp+68],ymm0
       vmovdqu   ymmword ptr [rsp+88],ymm0
       vmovdqu   ymmword ptr [rsp+0A8],ymm0
       vmovdqu   ymmword ptr [rsp+0C8],ymm0
       vmovdqu   ymmword ptr [rsp+0E8],ymm0
       vmovdqu   ymmword ptr [rsp+100],ymm0
       xor       edx,edx
       mov       [rsp+38],edx
       mov       [rsp+3C],edx
       mov       [rsp+40],edx
       lea       rdx,[rsp+140]
       mov       [rsp+120],rdx
       mov       dword ptr [rsp+128],8
       lea       rdx,[rsp+140]
       mov       [rsp+130],rdx
       mov       dword ptr [rsp+138],8
       mov       rdx,[rcx+18]
       mov       rsi,[rdx+28]
       test      rsi,rsi
       je        near ptr M15_L07
M15_L01:
       mov       rdx,rsi
       lea       rcx,[rsp+38]
       mov       r8,rbx
       call      qword ptr [7FF8B0397120]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].AddNonICollectionRangeInlined(System.Collections.Generic.IEnumerable`1<System.__Canon>)
       mov       rbx,rsi
       mov       edi,[rsp+3C]
       add       edi,[rsp+40]
       jo        near ptr M15_L30
       test      edi,edi
       je        near ptr M15_L08
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],118
       jle       near ptr M15_L13
       mov       rcx,[rcx+118]
       test      rcx,rcx
       je        near ptr M15_L13
M15_L02:
       mov       rdx,[rcx+18]
       mov       rax,[rdx+18]
       test      rax,rax
       je        near ptr M15_L14
       mov       rcx,rax
M15_L03:
       movsxd    rdx,edi
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rbp,rax
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],0E0
       jle       near ptr M15_L15
       mov       rcx,[rcx+0E0]
       test      rcx,rcx
       je        near ptr M15_L15
M15_L04:
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+30]
       test      rax,rax
       je        near ptr M15_L16
M15_L05:
       cmp       [rbp],rax
       jne       near ptr M15_L28
       lea       r14,[rbp+10]
       mov       r15d,[rbp+8]
       mov       r13,r14
       mov       r12d,r15d
       mov       edi,[rsp+38]
       test      edi,edi
       jne       near ptr M15_L17
M15_L06:
       mov       ecx,[rsp+40]
       cmp       ecx,[rsp+138]
       ja        near ptr M15_L26
       mov       rdx,[rsp+130]
       cmp       ecx,r12d
       ja        near ptr M15_L27
       mov       r8d,ecx
       shl       r8,3
       cmp       r8,4000
       ja        near ptr M15_L24
       mov       rcx,r13
       call      System.Buffer.__BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       short M15_L10
M15_L07:
       mov       rdx,7FF8B06AA600
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rsi,rax
       jmp       near ptr M15_L01
M15_L08:
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],110
       jle       short M15_L12
       mov       rcx,[rcx+110]
       test      rcx,rcx
       je        short M15_L12
M15_L09:
       call      qword ptr [7FF8AF9674C8]; System.Array.Empty[[System.__Canon, System.Private.CoreLib]]()
       mov       rbp,rax
M15_L10:
       mov       rdx,rsi
       mov       r8d,[rsp+38]
       test      r8d,r8d
       jne       near ptr M15_L25
M15_L11:
       mov       rax,rbp
       add       rsp,188
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M15_L12:
       mov       rcx,rbx
       mov       rdx,7FF8B06AA628
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       short M15_L09
M15_L13:
       mov       rcx,rbx
       mov       rdx,7FF8B06AA660
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M15_L02
M15_L14:
       mov       rdx,7FF8B065DC60
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M15_L03
M15_L15:
       mov       rcx,rbx
       mov       rdx,7FF8B0590820
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M15_L04
M15_L16:
       mov       rdx,7FF8B065ABD8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       near ptr M15_L05
M15_L17:
       mov       rdx,[rsp+120]
       mov       r12d,[rsp+128]
       cmp       r12d,r15d
       ja        near ptr M15_L27
       mov       r13d,r12d
       shl       r13,3
       mov       r8,r13
       mov       rcx,r14
       call      qword ptr [7FF8AF615740]; System.Buffer.BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
       add       r13,r14
       sub       r15d,r12d
       mov       r12d,r15d
       dec       edi
       je        near ptr M15_L06
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],0F0
       jle       short M15_L19
       mov       rdx,[rcx+0F0]
       test      rdx,rdx
       je        short M15_L19
M15_L18:
       lea       rcx,[rsp+28]
       lea       r8,[rsp+48]
       mov       r9d,1B
       call      qword ptr [7FF8AFEA6BF8]; <PrivateImplementationDetails>.InlineArrayAsReadOnlySpan[[System.Collections.Generic.SegmentedArrayBuilder`1+Arrays[[System.__Canon, System.Private.CoreLib]], System.Linq],[System.__Canon, System.Private.CoreLib]](Arrays<System.__Canon> ByRef, Int32)
       cmp       edi,[rsp+30]
       jbe       short M15_L20
       jmp       near ptr M15_L26
M15_L19:
       mov       rcx,rbx
       mov       rdx,7FF8B0590840
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
       jmp       short M15_L18
M15_L20:
       mov       rbx,[rsp+28]
       test      edi,edi
       jle       near ptr M15_L06
       xor       r14d,r14d
M15_L21:
       mov       r8,[rbx+r14]
       test      r8,r8
       je        short M15_L23
       lea       rdx,[r8+10]
       mov       r15d,[r8+8]
M15_L22:
       cmp       r15d,r12d
       ja        short M15_L27
       mov       eax,r15d
       shl       rax,3
       mov       [rsp+20],rax
       mov       r8,rax
       mov       rcx,r13
       call      qword ptr [7FF8AF615740]; System.Buffer.BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rcx,[rsp+20]
       add       r13,rcx
       sub       r12d,r15d
       add       r14,8
       dec       edi
       jne       short M15_L21
       jmp       near ptr M15_L06
M15_L23:
       xor       edx,edx
       xor       r15d,r15d
       jmp       short M15_L22
M15_L24:
       mov       rcx,r13
       call      qword ptr [7FF8B0506478]
       jmp       near ptr M15_L10
M15_L25:
       lea       rcx,[rsp+38]
       call      qword ptr [7FF8AFEA6D48]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].ReturnArrays(Int32)
       jmp       near ptr M15_L11
M15_L26:
       call      qword ptr [7FF8AF827798]
       int       3
M15_L27:
       call      qword ptr [7FF8AFAB5818]
       int       3
M15_L28:
       call      qword ptr [7FF8B050C0A8]
       int       3
M15_L29:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M15_L30:
       call      CORINFO_HELP_OVERFLOW
       int       3
; Total bytes of code 1055
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,60
       lea       rbp,[rsp+90]
       xor       eax,eax
       mov       [rbp-58],rax
       mov       [rbp-70],rsp
       mov       [rbp-38],rcx
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       [rbp+20],r8
       mov       [rbp+28],r9
       mov       rbx,[rbp+30]
       mov       rax,[rbp+18]
       mov       rax,[rax+8]
       mov       [rbp-58],rax
       movzx     eax,byte ptr [rbp+28]
       mov       r8d,[rbp+2C]
       test      eax,eax
       je        short M16_L02
M16_L00:
       mov       [rbp-3C],r8d
M16_L01:
       mov       rax,[rbp+18]
       mov       rcx,[rax+18]
       mov       [rbp-60],rcx
       mov       r10,[rbp+10]
       cmp       [r10],r10d
       mov       rax,[rbp+18]
       mov       r8,[rax+10]
       mov       rax,[rbp+18]
       mov       r9d,[rbp-3C]
       imul      r9,[rax+28]
       shr       r9,20
       inc       r9
       mov       r11d,[r8+8]
       mov       ebx,r11d
       imul      r9,rbx
       shr       r9,20
       mov       eax,r9d
       xor       edx,edx
       div       dword ptr [rcx+8]
       mov       [rbp-40],edx
       cmp       r9d,r11d
       jae       near ptr M16_L38
       mov       ecx,r9d
       lea       rbx,[r8+rcx*8+10]
       xor       esi,esi
       xor       edi,edi
       xor       ecx,ecx
       mov       [rbp-48],ecx
       jmp       short M16_L07
M16_L02:
       cmp       byte ptr [rcx+15],0
       jne       short M16_L06
       mov       rax,[rcx]
       mov       r8,[rax+30]
       mov       r8,[r8]
       mov       r11,[r8+0C0]
       test      r11,r11
       je        short M16_L05
M16_L03:
       mov       rcx,[rbp-58]
       mov       rdx,[rbp+20]
       call      qword ptr [r11]
       mov       r8d,eax
M16_L04:
       mov       rcx,[rbp+10]
       jmp       near ptr M16_L00
M16_L05:
       mov       rcx,rax
       mov       rdx,7FF8B0439B40
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M16_L03
M16_L06:
       mov       rdx,[rbp+20]
       mov       rcx,rdx
       mov       rax,[rdx]
       mov       rax,[rax+40]
       call      qword ptr [rax+18]
       mov       r8d,eax
       jmp       short M16_L04
M16_L07:
       cmp       byte ptr [rbp+40],0
       je        short M16_L08
       mov       rdx,[rbp-60]
       mov       edx,[rdx+8]
       cmp       [rbp-40],edx
       jae       near ptr M16_L21
       mov       rdx,[rbp-60]
       mov       ecx,[rbp-40]
       mov       rcx,[rdx+rcx*8+10]
       cmp       byte ptr [rbp-48],0
       jne       near ptr M16_L13
       lea       rdx,[rbp-48]
       call      System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
       mov       r10,[rbp+10]
M16_L08:
       mov       rcx,[rbp+18]
       cmp       rcx,[r10+8]
       jne       near ptr M16_L22
       xor       r14d,r14d
       mov       r15,[rbx]
       test      r15,r15
       jne       near ptr M16_L14
M16_L09:
       mov       rcx,[r10]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rdx,[rdx+0E8]
       test      rdx,rdx
       je        near ptr M16_L12
M16_L10:
       mov       rcx,rdx
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       r13,[rbx]
       lea       rcx,[r15+8]
       mov       rdx,[rbp+20]
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+10]
       mov       rdx,[rbp+30]
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r15+18]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       ecx,[rbp-3C]
       mov       [r15+20],ecx
       mov       rcx,rbx
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+18]
       mov       rcx,[rcx+20]
       mov       eax,[rcx+8]
       cmp       [rbp-40],eax
       jae       near ptr M16_L21
       mov       eax,[rbp-40]
       lea       rcx,[rcx+rax*4+10]
       mov       eax,[rcx]
       add       eax,1
       jo        near ptr M16_L30
       mov       [rcx],eax
       mov       r10,[rbp+10]
       cmp       eax,[r10+10]
       jg        near ptr M16_L28
M16_L11:
       cmp       r14d,64
       jbe       near ptr M16_L31
       jmp       near ptr M16_L29
M16_L12:
       mov       rdx,7FF8B0592B98
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
       jmp       near ptr M16_L10
M16_L13:
       call      qword ptr [7FF8B0506658]
       int       3
M16_L14:
       mov       ecx,[rbp-3C]
       cmp       ecx,[r15+20]
       jne       near ptr M16_L27
       mov       rcx,[r10]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+0B0]
       test      rax,rax
       je        short M16_L15
       mov       rcx,rax
       jmp       short M16_L16
M16_L15:
       mov       rdx,7FF8B0439B10
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
M16_L16:
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0B8]
       test      r11,r11
       je        short M16_L17
       jmp       short M16_L18
M16_L17:
       mov       rdx,7FF8B0439B28
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M16_L18:
       mov       rdx,[r15+8]
       mov       rcx,[rbp-58]
       mov       r8,[rbp+20]
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M16_L27
       cmp       byte ptr [rbp+38],0
       je        short M16_L19
       lea       rcx,[r15+10]
       mov       rdx,[rbp+30]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+48]
       mov       rdx,[rbp+30]
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       jmp       short M16_L20
M16_L19:
       mov       rdx,[r15+10]
       mov       rcx,[rbp+48]
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
M16_L20:
       xor       ecx,ecx
       mov       [rbp-4C],ecx
       jmp       near ptr M16_L37
M16_L21:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
M16_L22:
       mov       rcx,[r10+8]
       mov       [rbp+18],rcx
       mov       rcx,[rbp-58]
       mov       rdx,[rbp+18]
       cmp       rcx,[rdx+8]
       je        near ptr M16_L35
       mov       rcx,[rbp+18]
       mov       rcx,[rcx+8]
       mov       [rbp-58],rcx
       cmp       byte ptr [r10+15],0
       jne       short M16_L25
       mov       rcx,[r10]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0C0]
       test      r11,r11
       je        short M16_L23
       mov       r10,[rbp+10]
       jmp       short M16_L24
M16_L23:
       mov       rdx,7FF8B0439B40
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M16_L24:
       mov       rcx,[rbp-58]
       mov       rdx,[rbp+20]
       call      qword ptr [r11]
       jmp       short M16_L26
M16_L25:
       mov       rcx,[rbp+20]
       mov       rax,[rcx]
       mov       rax,[rax+40]
       call      qword ptr [rax+18]
M16_L26:
       mov       [rbp-3C],eax
       mov       r10,[rbp+10]
       jmp       near ptr M16_L35
M16_L27:
       mov       r10,[rbp+10]
       inc       r14d
       mov       r15,[r15+18]
       test      r15,r15
       jne       near ptr M16_L14
       jmp       near ptr M16_L09
M16_L28:
       mov       r10,[rbp+10]
       mov       esi,1
       jmp       near ptr M16_L11
M16_L29:
       mov       rdx,[rbp-58]
       mov       rcx,offset MT_System.Collections.Generic.NonRandomizedStringEqualityComparer
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       ecx,1
       test      rax,rax
       cmovne    edi,ecx
       jmp       short M16_L31
M16_L30:
       call      CORINFO_HELP_OVERFLOW
       int       3
M16_L31:
       cmp       byte ptr [rbp-48],0
       je        short M16_L32
       mov       rcx,[rbp-60]
       mov       ecx,[rcx+8]
       cmp       [rbp-40],ecx
       jae       short M16_L38
       mov       rcx,[rbp-60]
       mov       eax,[rbp-40]
       mov       rcx,[rcx+rax*8+10]
       call      System.Threading.Monitor.Exit(System.Object)
M16_L32:
       mov       ecx,esi
       or        ecx,edi
       jne       short M16_L34
M16_L33:
       mov       rcx,[rbp+48]
       mov       rdx,[rbp+30]
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       eax,1
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L34:
       mov       r10,[rbp+10]
       mov       rcx,r10
       mov       rdx,[rbp+18]
       mov       r8d,esi
       mov       r9d,edi
       call      qword ptr [7FF8AF96E2E0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GrowTable(Tables<System.__Canon,System.__Canon>, Boolean, Boolean)
       jmp       short M16_L33
M16_L35:
       mov       rcx,rsp
       call      M16_L39
       jmp       near ptr M16_L01
M16_L36:
       mov       eax,[rbp-4C]
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L37:
       mov       rcx,rsp
       call      M16_L39
       jmp       short M16_L36
M16_L38:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
M16_L39:
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+90]
       cmp       byte ptr [rbp-48],0
       je        short M16_L40
       mov       rcx,[rbp-60]
       mov       ecx,[rcx+8]
       cmp       [rbp-40],ecx
       jae       short M16_L41
       mov       rcx,[rbp-60]
       mov       eax,[rbp-40]
       mov       rcx,[rcx+rax*8+10]
       call      System.Threading.Monitor.Exit(System.Object)
M16_L40:
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M16_L41:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 1186
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValue(System.__Canon, System.__Canon ByRef)
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        near ptr M17_L09
       mov       rbp,[rbx+8]
       mov       r14,[rbp+8]
       cmp       byte ptr [rbx+15],0
       jne       near ptr M17_L05
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0C0]
       test      r11,r11
       je        near ptr M17_L04
M17_L00:
       mov       rcx,r14
       mov       rdx,rsi
       call      qword ptr [r11]
       mov       r15d,eax
M17_L01:
       mov       rcx,[rbp+10]
       mov       edx,r15d
       imul      rdx,[rbp+28]
       shr       rdx,20
       inc       rdx
       mov       eax,[rcx+8]
       mov       r8d,eax
       imul      rdx,r8
       shr       rdx,20
       cmp       edx,eax
       jae       near ptr M17_L10
       mov       edx,edx
       mov       rbp,[rcx+rdx*8+10]
       test      rbp,rbp
       je        near ptr M17_L08
M17_L02:
       cmp       r15d,[rbp+20]
       jne       near ptr M17_L07
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0B8]
       test      r11,r11
       je        short M17_L06
M17_L03:
       mov       rdx,[rbp+8]
       mov       rcx,r14
       mov       r8,rsi
       call      qword ptr [r11]
       test      eax,eax
       je        short M17_L07
       mov       rdx,[rbp+10]
       mov       rcx,rdi
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       eax,1
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M17_L04:
       mov       rdx,7FF8B0439B40
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       near ptr M17_L00
M17_L05:
       mov       rcx,rsi
       mov       rax,[rsi]
       mov       rax,[rax+40]
       call      qword ptr [rax+18]
       mov       r15d,eax
       jmp       near ptr M17_L01
M17_L06:
       mov       rdx,7FF8B0439B28
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M17_L03
M17_L07:
       mov       rbp,[rbp+18]
       test      rbp,rbp
       jne       near ptr M17_L02
M17_L08:
       xor       eax,eax
       mov       [rdi],rax
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M17_L09:
       mov       ecx,1
       mov       rdx,7FF8AF997C50
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8AFB172A0]
       int       3
M17_L10:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 358
```
```assembly
; Hl7.Fhir.Utility.CacheItem`1[[System.__Canon, System.Private.CoreLib]].get_Value()
       push      rbx
       sub       rsp,30
       mov       rbx,rcx
       call      qword ptr [7FF8AFB16EF8]; System.DateTime.get_UtcNow()
       mov       rdx,rax
       lea       rcx,[rsp+20]
       mov       r8d,1
       call      qword ptr [7FF8B0395920]; System.DateTimeOffset.ToLocalTime(System.DateTime, Boolean)
       vmovups   xmm0,[rsp+20]
       vmovups   [rbx+10],xmm0
       mov       rax,[rbx+8]
       add       rsp,30
       pop       rbx
       ret
; Total bytes of code 55
```
```assembly
; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ExecutionAndPublication(System.LazyHelper, Boolean)
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+50]
       mov       [rbp-30],rsp
       mov       rsi,rcx
       mov       rbx,rdx
       mov       edi,r8d
       mov       [rbp-28],rbx
       xor       edx,edx
       mov       [rbp-20],edx
       cmp       byte ptr [rbp-20],0
       jne       short M19_L01
       lea       rdx,[rbp-20]
       mov       rcx,rbx
       call      System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
       cmp       [rsi+8],rbx
       jne       short M19_L02
       test      dil,dil
       jne       short M19_L00
       mov       rcx,rsi
       mov       edx,2
       call      qword ptr [7FF8AF965290]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ViaFactory(System.Threading.LazyThreadSafetyMode)
       jmp       short M19_L02
M19_L00:
       mov       rcx,rsi
       call      qword ptr [7FF8B0506640]
       jmp       short M19_L02
M19_L01:
       call      qword ptr [7FF8B0506658]
       int       3
M19_L02:
       cmp       byte ptr [rbp-20],0
       je        short M19_L03
       mov       rcx,rbx
       call      System.Threading.Monitor.Exit(System.Object)
M19_L03:
       nop
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+50]
       cmp       byte ptr [rbp-20],0
       je        short M19_L04
       mov       rcx,[rbp-28]
       call      System.Threading.Monitor.Exit(System.Object)
M19_L04:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
; Total bytes of code 168
```
```assembly
; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].ViaFactory(System.Threading.LazyThreadSafetyMode)
       push      rbp
       push      rsi
       push      rbx
       sub       rsp,30
       lea       rbp,[rsp+40]
       mov       [rbp-18],rsp
       mov       [rbp+10],rcx
       mov       [rbp+18],edx
       mov       rbx,[rcx+10]
       test      rbx,rbx
       je        short M20_L02
       xor       eax,eax
       mov       [rcx+10],rax
       mov       rax,offset Hl7.Fhir.Utility.AnnotationList+<>c.<.ctor>b__13_0()
       cmp       [rbx+18],rax
       jne       short M20_L01
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       xor       ecx,ecx
       mov       [rsp+20],rcx
       mov       rcx,rsi
       mov       edx,1C
       mov       r8d,1F
       mov       r9d,1
       call      qword ptr [7FF8AF967528]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, Int32, Boolean, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       rdx,rsi
M20_L00:
       mov       rcx,[rbp+10]
       lea       rcx,[rcx+18]
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       rax,[rbp+10]
       mov       [rax+8],rcx
       jmp       short M20_L03
M20_L01:
       mov       rcx,[rbx+8]
       call      qword ptr [rbx+18]
       mov       rdx,rax
       jmp       short M20_L00
M20_L02:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B0506670]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M20_L03:
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rbp
       ret
       push      rbp
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+28]
       mov       [rsp+28],rbp
       lea       rbp,[rbp+40]
       mov       rbx,rdx
       mov       rcx,offset MT_System.LazyHelper
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       edx,[rbp+18]
       mov       r8,rbx
       call      qword ptr [7FF8B0506688]
       mov       rcx,[rbp+10]
       lea       rcx,[rcx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       call      CORINFO_HELP_RETHROW
       int       3
; Total bytes of code 276
```
```assembly
; System.Linq.Enumerable.TryGetFirstNonIterator[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
       push      rbp
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,40
       lea       rbp,[rsp+60]
       mov       [rbp-40],rsp
       mov       [rbp-28],rcx
       mov       [rbp+20],r8
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],38
       jle       short M21_L03
       mov       rcx,[rcx+38]
       test      rcx,rcx
       je        short M21_L03
M21_L00:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rdi,rax
       test      rdi,rdi
       je        near ptr M21_L06
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],50
       jle       short M21_L04
       mov       r11,[rcx+50]
       test      r11,r11
       je        short M21_L04
M21_L01:
       mov       rcx,rdi
       call      qword ptr [r11]
       test      eax,eax
       jle       near ptr M21_L14
       mov       r8,[rbp+20]
       mov       byte ptr [r8],1
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],58
       jle       short M21_L05
       mov       r11,[rcx+58]
       test      r11,r11
       je        short M21_L05
M21_L02:
       mov       rcx,rdi
       xor       edx,edx
       call      qword ptr [r11]
       nop
       add       rsp,40
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
M21_L03:
       mov       rcx,rbx
       mov       rdx,7FF8B0439D68
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M21_L00
M21_L04:
       mov       rcx,rbx
       mov       rdx,7FF8B043A0D0
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M21_L01
M21_L05:
       mov       rcx,rbx
       mov       rdx,7FF8B043A0E8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M21_L02
M21_L06:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],40
       jle       short M21_L08
       mov       r11,[rcx+40]
       test      r11,r11
       je        short M21_L08
M21_L07:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       [rbp-30],rax
       jmp       short M21_L09
M21_L08:
       mov       rcx,rbx
       mov       rdx,7FF8B0439EF8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M21_L07
M21_L09:
       mov       rsi,[rax]
       mov       rdi,offset MT_<>z__ReadOnlySingleElementList<Hl7.Fhir.Model.PocoNode>+Enumerator
       cmp       rsi,rdi
       jne       short M21_L13
       mov       rcx,rax
       call      qword ptr [7FF8B03CDED8]; <>z__ReadOnlySingleElementList`1+Enumerator[[System.__Canon, System.Private.CoreLib]].System.Collections.IEnumerator.MoveNext()
M21_L10:
       test      eax,eax
       je        near ptr M21_L18
       mov       r8,[rbp+20]
       mov       byte ptr [r8],1
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],48
       jle       short M21_L12
       mov       r11,[rcx+48]
       test      r11,r11
       je        short M21_L12
M21_L11:
       mov       rcx,[rbp-30]
       call      qword ptr [r11]
       mov       r14,rax
       jmp       short M21_L15
M21_L12:
       mov       rcx,rbx
       mov       rdx,7FF8B0439F10
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M21_L11
M21_L13:
       mov       rcx,rax
       mov       r11,7FF8AF5727F8
       call      qword ptr [r11]
       jmp       short M21_L10
M21_L14:
       mov       r8,[rbp+20]
       mov       byte ptr [r8],0
       xor       eax,eax
       add       rsp,40
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
M21_L15:
       cmp       rsi,rdi
       jne       short M21_L17
M21_L16:
       mov       rax,r14
       add       rsp,40
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
M21_L17:
       mov       rcx,[rbp-30]
       mov       r11,7FF8AF572800
       call      qword ptr [r11]
       jmp       short M21_L16
M21_L18:
       mov       rcx,rsp
       call      M21_L19
       jmp       short M21_L14
M21_L19:
       push      rbp
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+60]
       cmp       qword ptr [rbp-30],0
       je        short M21_L20
       mov       rcx,[rbp-30]
       mov       rsi,[rcx]
       mov       rdi,offset MT_<>z__ReadOnlySingleElementList<Hl7.Fhir.Model.PocoNode>+Enumerator
       cmp       rsi,rdi
       je        short M21_L20
       mov       rcx,[rbp-30]
       mov       r11,7FF8AF572800
       call      qword ptr [r11]
M21_L20:
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
; Total bytes of code 545
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastClassSpecial(Void*, System.Object)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rsi,rcx
       mov       rbx,rdx
       mov       rdi,[rbx]
M22_L00:
       mov       rdi,[rdi+10]
       cmp       rdi,rsi
       jne       short M22_L02
M22_L01:
       mov       rcx,7FF8B0533794
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,rbx
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M22_L02:
       test      rdi,rdi
       je        near ptr M22_L09
       mov       rdi,[rdi+10]
       cmp       rdi,rsi
       jne       short M22_L03
       mov       rcx,7FF8B0533790
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       short M22_L01
M22_L03:
       test      rdi,rdi
       je        short M22_L08
       mov       rdi,[rdi+10]
       cmp       rdi,rsi
       jne       short M22_L04
       mov       rcx,7FF8B0533788
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       short M22_L01
M22_L04:
       test      rdi,rdi
       je        short M22_L07
       mov       rdi,[rdi+10]
       cmp       rdi,rsi
       je        short M22_L05
       test      rdi,rdi
       je        short M22_L06
       mov       rcx,7FF8B053377C
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       near ptr M22_L00
M22_L05:
       mov       rcx,7FF8B0533780
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       near ptr M22_L01
M22_L06:
       mov       rcx,7FF8B0533778
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       short M22_L09
M22_L07:
       mov       rcx,7FF8B0533784
       call      CORINFO_HELP_COUNTPROFILE32
       jmp       short M22_L09
M22_L08:
       mov       rcx,7FF8B053378C
       call      CORINFO_HELP_COUNTPROFILE32
M22_L09:
       mov       rcx,7FF8B0533798
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rcx,rsi
       mov       rdx,rbx
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8B045EAD8]
; Total bytes of code 259
```
```assembly
; Hl7.Fhir.Introspection.ModelInspector+<>c__DisplayClass2_0.<ForAssembly>b__0(System.String)
       push      rbp
       sub       rsp,20
       lea       rbp,[rsp+20]
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8AFC8ECB8]; Hl7.Fhir.Introspection.ModelInspector+<>c__DisplayClass2_0.<ForAssembly>g__configureInspector|1()
       nop
       add       rsp,20
       pop       rbp
       ret
; Total bytes of code 35
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.classMappingForElement(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.PropertyMapping, System.Type)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rsp+20],xmm4
       xor       eax,eax
       mov       [rsp+30],rax
       mov       rdi,rcx
       mov       rbx,rdx
       mov       rsi,r8
       mov       rbp,r9
       test      rsi,rsi
       je        near ptr M24_L07
       mov       r14,[rsi+28]
       mov       r8,[rdi+8]
       mov       r8,[r8+20]
       mov       rcx,[r8+18]
       test      rcx,rcx
       je        near ptr M24_L25
       lea       r8,[rsp+28]
       mov       rdx,r14
       mov       r11,7FF8AF572B10
       call      qword ptr [r11]
       xor       r15d,r15d
       test      eax,eax
       cmovne    r15,[rsp+28]
       xor       ecx,ecx
       mov       [rsp+28],rcx
       test      r15,r15
       je        near ptr M24_L06
M24_L00:
       xor       ecx,ecx
       mov       [rsp+30],rcx
M24_L01:
       mov       r13,rbx
       test      r13,r13
       je        short M24_L02
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [r13],rcx
       jne       near ptr M24_L19
       xor       r13d,r13d
M24_L02:
       test      r13,r13
       jne       near ptr M24_L20
M24_L03:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rbx],rcx
       jne       near ptr M24_L22
       mov       r12,[rbx+10]
M24_L04:
       test      r12,r12
       je        near ptr M24_L26
       test      r15,r15
       je        near ptr M24_L08
       mov       rcx,r15
       call      qword ptr [7FF8B017DD88]; Hl7.Fhir.Specification.TypeSerializationInfoExtensions.GetTypeName(Hl7.Fhir.Specification.ITypeSerializationInfo)
M24_L05:
       cmp       r12,rax
       je        near ptr M24_L27
       test      rax,rax
       je        near ptr M24_L11
       mov       ecx,[r12+8]
       cmp       ecx,[rax+8]
       jne       short M24_L11
       lea       rcx,[r12+0C]
       lea       rdx,[rax+0C]
       mov       r8d,[r12+8]
       add       r8d,r8d
       cmp       r8,0A
       jne       short M24_L09
       mov       r8,[rcx]
       mov       rcx,[rcx+2]
       mov       rax,[rdx]
       xor       r8,rax
       xor       rcx,[rdx+2]
       or        rcx,r8
       sete      r13b
       movzx     r13d,r13b
       jmp       short M24_L10
M24_L06:
       mov       rcx,[rdi+8]
       lea       r8,[rsp+30]
       mov       rdx,r14
       call      qword ptr [7FF8AFEA6070]; Hl7.Fhir.Introspection.ClassMapping.TryCreate(Hl7.Fhir.Introspection.ModelInspector, System.Type, Hl7.Fhir.Introspection.ClassMapping ByRef)
       test      eax,eax
       je        near ptr M24_L18
       mov       r15,[rsp+30]
       jmp       near ptr M24_L00
M24_L07:
       xor       r15d,r15d
       jmp       near ptr M24_L01
M24_L08:
       xor       eax,eax
       jmp       near ptr M24_L05
M24_L09:
       call      qword ptr [7FF8AF61C090]; System.SpanHelpers.SequenceEqual(Byte ByRef, Byte ByRef, UIntPtr)
       mov       r13d,eax
M24_L10:
       test      r13d,r13d
       jne       near ptr M24_L27
M24_L11:
       cmp       dword ptr [r12+8],4
       jne       short M24_L12
       mov       rcx,650064006F0063
       cmp       [r12+0C],rcx
       je        short M24_L13
M24_L12:
       mov       rcx,r12
       mov       rdx,26D0039D2D8
       xor       r8d,r8d
       call      qword ptr [7FF8AF61D680]; System.String.StartsWith(System.String, System.StringComparison)
       test      eax,eax
       jne       near ptr M24_L17
       mov       r8,[rdi+8]
       mov       r8,[r8+20]
       mov       rcx,[r8+8]
       test      rcx,rcx
       jne       short M24_L15
       jmp       near ptr M24_L25
M24_L13:
       test      r15,r15
       jne       near ptr M24_L23
       xor       eax,eax
       xor       r13d,r13d
M24_L14:
       test      al,al
       je        short M24_L12
       jmp       near ptr M24_L24
M24_L15:
       lea       r8,[rsp+20]
       mov       rdx,r12
       mov       r11,7FF8AF572B18
       call      qword ptr [r11]
       xor       r14d,r14d
       test      eax,eax
       cmovne    r14,[rsp+20]
       xor       edx,edx
       mov       [rsp+20],rdx
       test      r14,r14
       je        short M24_L17
       mov       rdx,[r14+20]
       mov       rcx,26D002DCF38
       call      qword ptr [7FF8AF56A4C8]; System.RuntimeType.IsAssignableFrom(System.Type)
       test      eax,eax
       je        short M24_L17
       mov       rax,r14
M24_L16:
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M24_L17:
       test      rbp,rbp
       jne       near ptr M24_L29
       test      r15,r15
       jne       near ptr M24_L28
       mov       rcx,rdi
       mov       rdx,rbx
       call      qword ptr [7FF8B017DDE8]; Hl7.Fhir.ElementModel.NewPocoBuilder.determineBestDynamicMappingForElement(Hl7.Fhir.ElementModel.ITypedElement)
       jmp       short M24_L16
M24_L18:
       mov       ecx,10C34
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rbx,rax
       mov       rcx,r14
       mov       rax,[r14]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       r15,rax
       mov       ecx,1CAC
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rcx,rbx
       mov       rdx,r15
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rsi,rax
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       rcx,rdi
       mov       rdx,rsi
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rdi
       call      CORINFO_HELP_THROW
       int       3
M24_L19:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r13,rax
       jmp       near ptr M24_L02
M24_L20:
       mov       rdx,[r13+10]
       mov       rcx,offset MT_Hl7.Fhir.Model.IDynamicType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        near ptr M24_L03
       test      r15,r15
       je        near ptr M24_L03
       mov       rcx,[r15+20]
       test      rcx,rcx
       je        short M24_L21
       mov       rax,[rcx]
       mov       rax,[rax+70]
       call      qword ptr [rax+18]
       test      al,80
       je        near ptr M24_L27
M24_L21:
       mov       rcx,rbx
       mov       r11,7FF8AF572B08
       call      qword ptr [r11]
       mov       rcx,rax
       mov       rdx,[rsi+8]
       mov       edx,[rdx+8]
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF965620]; System.String.Substring(Int32)
       test      rax,rax
       je        near ptr M24_L03
       cmp       dword ptr [rax+8],0
       jle       near ptr M24_L03
       mov       rcx,[rdi+8]
       mov       rdx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFEAFE88]; Hl7.Fhir.Introspection.ModelInspector.FindClassMapping(System.String)
       test      rax,rax
       je        near ptr M24_L03
       jmp       near ptr M24_L16
M24_L22:
       mov       rcx,rbx
       mov       r11,7FF8AF572B00
       call      qword ptr [r11]
       mov       r12,rax
       jmp       near ptr M24_L04
M24_L23:
       cmp       qword ptr [r15+28],0
       setne     r13b
       movzx     r13d,r13b
       mov       eax,1
       jmp       near ptr M24_L14
M24_L24:
       test      r13b,r13b
       jne       short M24_L27
       jmp       near ptr M24_L12
M24_L25:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
M24_L26:
       test      r15,r15
       je        near ptr M24_L17
       mov       rcx,[r15+20]
       test      rcx,rcx
       je        near ptr M24_L17
       mov       rax,[rcx]
       mov       rax,[rax+70]
       call      qword ptr [rax+18]
       test      al,80
       jne       near ptr M24_L17
       cmp       byte ptr [r15+58],0
       jne       near ptr M24_L17
M24_L27:
       mov       rax,r15
       jmp       near ptr M24_L16
M24_L28:
       mov       r8,[r15+20]
       mov       rcx,rdi
       mov       rdx,rbx
       call      qword ptr [7FF8B017DDD0]
       jmp       near ptr M24_L16
M24_L29:
       mov       rcx,rdi
       mov       rdx,rbp
       call      qword ptr [7FF8B017DD40]; Hl7.Fhir.ElementModel.NewPocoBuilder.getClassMapping(System.Type)
       jmp       near ptr M24_L16
; Total bytes of code 1048
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.readFromElement(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Introspection.ClassMapping)
M25_L00:
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0E8
       lea       rbp,[rsp+120]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-80],ymm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-0F8],rsp
       mov       rsi,rcx
       mov       rbx,rdx
       mov       rdi,r8
       mov       r14,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rbx],r14
       jne       near ptr M25_L89
       mov       rcx,offset MT_System.Func<System.Object>
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       lea       r13,[rbx+38]
       lea       r12,[rbx+50]
       lea       rcx,[r15+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
       mov       [r15+18],rdx
       cmp       byte ptr [r12],0
       je        near ptr M25_L20
       mov       rax,[r13]
M25_L01:
       test      rax,rax
       setne     dl
       movzx     edx,dl
       mov       rcx,rdi
       call      qword ptr [7FF8B017E148]; Hl7.Fhir.ElementModel.NewPocoBuilder.buildNewInstance(Hl7.Fhir.Introspection.ClassMapping, Boolean)
       mov       r15,rax
       mov       r13,rbx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [r13],rcx
       jne       near ptr M25_L90
M25_L02:
       test      r13,r13
       je        near ptr M25_L06
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementAnnotatedProvider
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       lea       rcx,[r12+8]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       cmp       [r15],r15b
       lea       r13,[r15+10]
       mov       rcx,26D3D803660
       mov       r8,[rcx]
       test      r8,r8
       je        near ptr M25_L91
M25_L03:
       mov       rax,[r13]
       test      rax,rax
       je        near ptr M25_L21
M25_L04:
       mov       [rbp-98],rax
       cmp       [rax],al
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList+<>c__DisplayClass4_0
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       lea       rcx,[r13+8]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       mov       r12,[rbp-98]
       mov       rcx,[r12+8]
       cmp       qword ptr [rcx+8],0
       je        near ptr M25_L22
       call      qword ptr [7FF8AF965260]; System.Lazy`1[[System.__Canon, System.Private.CoreLib]].CreateValue()
       mov       r12,rax
M25_L05:
       mov       rcx,offset MT_System.Collections.Generic.List<System.Object>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0A0],rax
       mov       rcx,[r13+8]
       call      System.Object.GetType()
       mov       [rbp-0A8],rax
       mov       rcx,offset MT_System.Object[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       r8,[rbp-0A0]
       lea       rcx,[r8+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8d,1
       mov       rdx,[rbp-0A0]
       mov       rcx,7FF8B024E3E8
       call      qword ptr [7FF8AFEA6B80]; System.Runtime.InteropServices.CollectionsMarshal.SetCount[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.List`1<System.__Canon>, Int32)
       mov       rax,[rbp-0A0]
       mov       ecx,[rax+10]
       mov       rdx,[rax+8]
       cmp       [rdx+8],ecx
       jb        near ptr M25_L92
       add       rdx,10
       mov       [rbp-0E8],rdx
       test      ecx,ecx
       je        near ptr M25_L130
       mov       rdx,[r13+8]
       mov       rcx,[rbp-0E8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset MT_System.Func<System.Type, System.Collections.Generic.List<System.Object>, System.Collections.Generic.List<System.Object>>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0B0],rax
       lea       rcx,[rax+8]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,7FF8B017AC40
       mov       r13,[rbp-0B0]
       mov       [r13+18],rcx
       mov       rcx,r12
       mov       rdx,[rbp-0A8]
       mov       r8,[rbp-0A0]
       mov       r9,r13
       cmp       [rcx],ecx
       call      qword ptr [7FF8B017EC88]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AddOrUpdate(System.__Canon, System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>)
M25_L06:
       mov       r13,r15
       test      r13,r13
       je        short M25_L07
       mov       rcx,offset MT_Hl7.Fhir.Model.Code
       cmp       [r13],rcx
       jne       near ptr M25_L23
       xor       r13d,r13d
M25_L07:
       test      r13,r13
       jne       near ptr M25_L24
M25_L08:
       cmp       [rbx],r14
       jne       near ptr M25_L98
       mov       rcx,offset MT_System.Func<System.Object>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       lea       r13,[rbx+38]
       lea       rax,[rbx+50]
       mov       [rbp-0B8],rax
       lea       rcx,[r12+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
       mov       [r12+18],rcx
       mov       rax,[rbp-0B8]
       cmp       byte ptr [rax],0
       je        near ptr M25_L27
       mov       r13,[r13]
M25_L09:
       test      r13,r13
       je        near ptr M25_L16
       mov       rdx,r15
       test      rdx,rdx
       je        short M25_L10
       mov       rcx,offset MT_Hl7.Fhir.Model.FhirString
       cmp       [rdx],rcx
       jne       near ptr M25_L28
       xor       edx,edx
M25_L10:
       test      rdx,rdx
       jne       near ptr M25_L29
       mov       r8,rbx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [r8],rcx
       jne       near ptr M25_L99
       xor       r8d,r8d
M25_L11:
       test      r8,r8
       jne       near ptr M25_L100
M25_L12:
       mov       rcx,r13
       call      qword ptr [7FF8B017E1A8]; Hl7.Fhir.ElementModel.NewPocoBuilder.convertTypedElementValue(System.Object)
       mov       r12,rax
M25_L13:
       mov       rcx,r15
       test      rcx,rcx
       je        short M25_L14
       mov       rdx,offset MT_Hl7.Fhir.Model.FhirString
       cmp       [rcx],rdx
       jne       near ptr M25_L30
M25_L14:
       test      rcx,rcx
       je        near ptr M25_L108
       mov       rdx,offset MT_Hl7.Fhir.Model.FhirString
       cmp       [rcx],rdx
       jne       near ptr M25_L31
       lea       rcx,[rcx+30]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
M25_L15:
       mov       rcx,[rsi+10]
       test      rcx,rcx
       je        short M25_L16
       cmp       byte ptr [rcx+10],0
       je        near ptr M25_L101
M25_L16:
       cmp       [rbx],r14
       jne       near ptr M25_L128
       mov       rcx,[rbx+10]
       test      rcx,rcx
       je        short M25_L17
       cmp       dword ptr [rcx+8],5
       jne       short M25_L17
       mov       rax,6D007400680078
       xor       rax,[rcx+0C]
       mov       ecx,[rcx+12]
       xor       ecx,6C006D
       or        rcx,rax
       je        near ptr M25_L32
M25_L17:
       mov       r12,[rbx+20]
       mov       r13,[rbx+30]
       test      r13,r13
       je        near ptr M25_L123
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [r13],rcx
       jne       near ptr M25_L34
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Specification.ITypeSerializationInfo[]>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0C0],rax
       lea       r8,[r13+58]
       mov       [rbp-0C8],r8
       lea       rcx,[rax+8]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,offset Hl7.Fhir.Introspection.PropertyMapping.buildTypes()
       mov       rax,[rbp-0C0]
       mov       [rax+18],rdx
       mov       r8,[rbp-0C8]
       mov       rdx,[r8]
       test      rdx,rdx
       je        near ptr M25_L33
M25_L18:
       mov       r10,[r13+58]
M25_L19:
       cmp       dword ptr [r10+8],0
       jbe       near ptr M25_L130
       mov       rdx,[r10+10]
       mov       rcx,offset MT_Hl7.Fhir.Specification.IStructureDefinitionSummary
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       jne       near ptr M25_L43
       mov       r13,[rbx+10]
       test      r13,r13
       je        near ptr M25_L123
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ModelInspector
       cmp       [r12],rcx
       jne       near ptr M25_L120
       lea       rcx,[r13+0C]
       mov       r8d,[r13+8]
       mov       edx,2F
       call      qword ptr [7FF8AFEAFEB8]; System.PackedSpanHelpers.Contains(Int16 ByRef, Int16, Int32)
       test      eax,eax
       jne       near ptr M25_L119
       mov       r8,[r12+20]
       mov       rcx,[r8+8]
       test      rcx,rcx
       jne       near ptr M25_L35
       jmp       near ptr M25_L118
M25_L20:
       mov       [rsp+20],r15
       mov       rdx,r13
       mov       r8,r12
       mov       rcx,7FF8B0245968
       mov       r9,26D3D801FD0
       call      qword ptr [7FF8B017DF50]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Boolean ByRef, System.Object ByRef, System.Func`1<System.__Canon>)
       jmp       near ptr M25_L01
M25_L21:
       mov       rdx,r13
       mov       rcx,7FF8B024D7C0
       call      qword ptr [7FF8B017E310]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       mov       r13,rax
       mov       rax,r13
       jmp       near ptr M25_L04
M25_L22:
       mov       r12,[rcx+18]
       jmp       near ptr M25_L05
M25_L23:
       mov       rdx,r15
       mov       rcx,offset MT_Hl7.Fhir.Model.IDynamicType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       r13,rax
       jmp       near ptr M25_L07
M25_L24:
       cmp       [rbx],r14
       jne       near ptr M25_L93
       mov       rdx,[rbx+10]
M25_L25:
       test      rdx,rdx
       je        near ptr M25_L94
M25_L26:
       mov       rcx,offset MT_Hl7.Fhir.Model.DynamicResource
       cmp       [r13],rcx
       jne       near ptr M25_L97
       lea       rcx,[r13+60]
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M25_L08
M25_L27:
       mov       [rsp+20],r12
       mov       rdx,r13
       mov       r8,rax
       mov       rcx,7FF8B0245968
       mov       r9,26D3D801FD0
       call      qword ptr [7FF8B017DF50]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Boolean ByRef, System.Object ByRef, System.Func`1<System.__Canon>)
       mov       r13,rax
       jmp       near ptr M25_L09
M25_L28:
       mov       rdx,r15
       mov       rcx,offset MT_Hl7.Fhir.Model.DynamicPrimitive
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rdx,rax
       jmp       near ptr M25_L10
M25_L29:
       mov       r12,r13
       jmp       near ptr M25_L13
M25_L30:
       mov       rdx,r15
       mov       rcx,offset MT_Hl7.Fhir.Model.PrimitiveType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M25_L14
M25_L31:
       mov       rdx,r12
       mov       rax,[rcx]
       mov       rax,[rax+50]
       call      qword ptr [rax+28]
       jmp       near ptr M25_L15
M25_L32:
       mov       rcx,rbx
       call      qword ptr [7FF8B0075B90]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.get_Name()
       test      rax,rax
       je        near ptr M25_L127
       cmp       dword ptr [rax+8],3
       jne       near ptr M25_L127
       mov       r8d,[rax+0C]
       xor       r8d,690064
       mov       edx,[rax+0E]
       xor       edx,760069
       or        r8d,edx
       je        near ptr M25_L17
       jmp       near ptr M25_L127
M25_L33:
       mov       r8,rax
       mov       rdx,[rbp-0C8]
       mov       rcx,7FF8B02AAA20
       call      qword ptr [7FF8B017E310]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       jmp       near ptr M25_L18
M25_L34:
       mov       rcx,r13
       mov       r11,7FF8AF572BB8
       call      qword ptr [r11]
       mov       r10,rax
       jmp       near ptr M25_L19
M25_L35:
       lea       r8,[rbp-68]
       mov       rdx,r13
       mov       r11,7FF8AF572BE8
       call      qword ptr [r11]
       xor       ecx,ecx
       test      eax,eax
       cmovne    rcx,[rbp-68]
       xor       eax,eax
       mov       [rbp-68],rax
M25_L36:
       test      rcx,rcx
       je        near ptr M25_L123
       mov       rax,offset MT_Hl7.Fhir.Introspection.ClassMapping
       cmp       [rcx],rax
       jne       near ptr M25_L121
       call      qword ptr [7FF8AFDE48D8]; Hl7.Fhir.Introspection.ClassMapping.Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements()
       mov       r12,rax
M25_L37:
       mov       r8,26D3D803610
       mov       r8,[r8]
       test      r8,r8
       je        near ptr M25_L124
M25_L38:
       mov       rdx,r12
       mov       rcx,7FF8B028CA30
       xor       r9d,r9d
       call      qword ptr [7FF8B0275038]; System.Linq.Enumerable.ToDictionary[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,System.__Canon>, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       r12,rax
       cmp       qword ptr [rbx+30],0
       je        short M25_L39
       mov       rdx,r12
       mov       rcx,7FF8B024F488
       call      qword ptr [7FF8B017ED48]; System.Linq.Enumerable.Any[[System.Collections.Generic.KeyValuePair`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]], System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>)
       test      eax,eax
       je        near ptr M25_L125
M25_L39:
       mov       r13,[rbx+18]
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<enumerateElements>d__35
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0D0],rax
       mov       dword ptr [rax+58],0FFFFFFFE
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       r8,[rbp-0D0]
       mov       [r8+5C],eax
       lea       rcx,[r8+40]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-0D0]
       lea       rcx,[rax+38]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       mov       r12,[rbp-0D0]
       lea       rcx,[r12+28]
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [r12+18],rcx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       dword ptr [r13+40],0FFFFFFFE
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r13+44],eax
       lea       rcx,[r13+10]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r13+20]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
M25_L40:
       mov       rcx,r13
       mov       rax,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37
       cmp       [rcx],rax
       jne       near ptr M25_L129
       cmp       dword ptr [r13+40],0FFFFFFFE
       jne       short M25_L45
       mov       ebx,[r13+44]
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       cmp       ebx,eax
       jne       short M25_L45
       xor       ecx,ecx
       mov       [r13+40],ecx
       mov       rbx,r13
M25_L41:
       mov       rdx,[r13+20]
       lea       rcx,[rbx+18]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,rbx
M25_L42:
       mov       [rbp-88],rcx
       jmp       short M25_L46
M25_L43:
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ClassMapping
       cmp       [rax],rcx
       jne       near ptr M25_L122
       mov       rcx,rax
       call      qword ptr [7FF8AFDE48D8]; Hl7.Fhir.Introspection.ClassMapping.Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements()
       mov       r12,rax
M25_L44:
       jmp       near ptr M25_L37
M25_L45:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FF8B0275188]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37..ctor(Int32)
       mov       rdx,[r13+10]
       lea       rcx,[rbx+10]
       call      CORINFO_HELP_ASSIGN_REF
       jmp       short M25_L41
M25_L46:
       mov       r11,7FF8AF572B40
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M25_L88
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37
       mov       rax,[rbp-88]
       cmp       [rax],rcx
       jne       near ptr M25_L75
       mov       r12,[rax+8]
       cmp       [r12],r14
       jne       near ptr M25_L76
       mov       rcx,[r12+30]
       test      rcx,rcx
       je        short M25_L49
       mov       rdx,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],rdx
       jne       near ptr M25_L73
       mov       rdx,[rcx+8]
M25_L47:
       test      rdx,rdx
       je        near ptr M25_L74
M25_L48:
       mov       rcx,rdi
       cmp       [rcx],ecx
       call      qword ptr [7FF8B017E208]; Hl7.Fhir.Introspection.ClassMapping.FindMappedElementByChoiceName(System.String)
       mov       r13,rax
       test      r13,r13
       je        near ptr M25_L59
       mov       rbx,[r13+28]
       xor       r8d,r8d
       mov       [rbp-70],r8
       mov       r8,[rsi+8]
       mov       r8,[r8+20]
       mov       rcx,[r8+18]
       test      rcx,rcx
       jne       short M25_L50
       jmp       near ptr M25_L84
M25_L49:
       xor       edx,edx
       jmp       short M25_L47
M25_L50:
       lea       r8,[rbp-78]
       mov       rdx,rbx
       mov       r11,7FF8AF572C10
       call      qword ptr [r11]
       test      eax,eax
       je        short M25_L51
       mov       rax,[rbp-78]
       jmp       short M25_L52
M25_L51:
       xor       eax,eax
M25_L52:
       xor       edx,edx
       mov       [rbp-78],rdx
       test      rax,rax
       je        near ptr M25_L58
M25_L53:
       xor       edx,edx
       mov       [rbp-70],rdx
       mov       rbx,rax
M25_L54:
       mov       rdx,r12
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M25_L79
M25_L55:
       cmp       [r12],r14
       jne       near ptr M25_L81
       mov       rdx,[r12+10]
M25_L56:
       mov       [rbp-0D8],rdx
       test      rdx,rdx
       je        near ptr M25_L85
       test      rbx,rbx
       je        near ptr M25_L61
       mov       rcx,rbx
       call      qword ptr [7FF8B017DD88]; Hl7.Fhir.Specification.TypeSerializationInfoExtensions.GetTypeName(Hl7.Fhir.Specification.ITypeSerializationInfo)
M25_L57:
       mov       r10,[rbp-0D8]
       cmp       r10,rax
       je        near ptr M25_L86
       test      rax,rax
       je        near ptr M25_L64
       mov       ecx,[r10+8]
       cmp       ecx,[rax+8]
       jne       near ptr M25_L64
       lea       rcx,[r10+0C]
       lea       rdx,[rax+0C]
       mov       [rbp-0D8],r10
       mov       r8d,[r10+8]
       add       r8d,r8d
       cmp       r8,0A
       jne       short M25_L62
       mov       r8,[rcx]
       mov       rcx,[rcx+2]
       mov       rax,[rdx]
       xor       r8,rax
       xor       rcx,[rdx+2]
       or        rcx,r8
       sete      al
       movzx     eax,al
       jmp       short M25_L63
M25_L58:
       mov       rcx,[rsi+8]
       lea       r8,[rbp-70]
       mov       rdx,rbx
       call      qword ptr [7FF8AFEA6070]; Hl7.Fhir.Introspection.ClassMapping.TryCreate(Hl7.Fhir.Introspection.ModelInspector, System.Type, Hl7.Fhir.Introspection.ClassMapping ByRef)
       test      eax,eax
       je        near ptr M25_L77
       mov       rax,[rbp-70]
       jmp       near ptr M25_L53
M25_L59:
       mov       rcx,[rsi+10]
       test      rcx,rcx
       je        short M25_L60
       cmp       byte ptr [rcx+11],0
       je        near ptr M25_L78
M25_L60:
       xor       ebx,ebx
       jmp       near ptr M25_L54
M25_L61:
       xor       eax,eax
       jmp       near ptr M25_L57
M25_L62:
       call      qword ptr [7FF8AF61C090]; System.SpanHelpers.SequenceEqual(Byte ByRef, Byte ByRef, UIntPtr)
M25_L63:
       test      eax,eax
       mov       r10,[rbp-0D8]
       jne       near ptr M25_L86
M25_L64:
       cmp       dword ptr [r10+8],4
       jne       short M25_L65
       mov       rcx,650064006F0063
       cmp       [r10+0C],rcx
       je        short M25_L66
M25_L65:
       cmp       [r10],r10b
       mov       [rbp-0D8],r10
       mov       rcx,r10
       mov       rdx,26D0039D2D8
       xor       r8d,r8d
       call      qword ptr [7FF8AF61D680]; System.String.StartsWith(System.String, System.StringComparison)
       test      eax,eax
       jne       near ptr M25_L72
       mov       r8,[rsi+8]
       mov       r8,[r8+20]
       mov       rcx,[r8+8]
       test      rcx,rcx
       jne       short M25_L68
       jmp       near ptr M25_L84
M25_L66:
       test      rbx,rbx
       jne       near ptr M25_L82
       xor       eax,eax
       xor       r9d,r9d
M25_L67:
       test      al,al
       je        short M25_L65
       jmp       near ptr M25_L83
M25_L68:
       mov       r10,[rbp-0D8]
       lea       r8,[rbp-80]
       mov       rdx,r10
       mov       r11,7FF8AF572C18
       call      qword ptr [r11]
       test      eax,eax
       je        short M25_L69
       mov       rax,[rbp-80]
       mov       r8,rax
       jmp       short M25_L70
M25_L69:
       xor       r8d,r8d
M25_L70:
       xor       edx,edx
       mov       [rbp-80],rdx
       test      r8,r8
       je        short M25_L72
       mov       [rbp-0E0],r8
       mov       rdx,[r8+20]
       mov       rcx,26D002DCF38
       call      qword ptr [7FF8AF56A4C8]; System.RuntimeType.IsAssignableFrom(System.Type)
       test      eax,eax
       je        short M25_L72
       mov       rbx,[rbp-0E0]
       mov       r8,rbx
M25_L71:
       mov       rcx,rsi
       mov       rdx,r12
       call      qword ptr [7FF8B017DD10]
       mov       r9,rax
       mov       [rsp+20],r13
       mov       rdx,r12
       mov       r8,r15
       mov       rcx,rsi
       call      qword ptr [7FF8B017E238]; Hl7.Fhir.ElementModel.NewPocoBuilder.setOrAddProperty(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Model.Base, Hl7.Fhir.Model.Base, Hl7.Fhir.Introspection.PropertyMapping)
       mov       rcx,[rbp-88]
       jmp       near ptr M25_L46
M25_L72:
       test      rbx,rbx
       jne       near ptr M25_L87
       mov       rcx,rsi
       mov       rdx,r12
       call      qword ptr [7FF8B017DDE8]; Hl7.Fhir.ElementModel.NewPocoBuilder.determineBestDynamicMappingForElement(Hl7.Fhir.ElementModel.ITypedElement)
       mov       r8,rax
       jmp       short M25_L71
M25_L73:
       mov       r11,7FF8AF572BF0
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M25_L47
M25_L74:
       mov       rcx,[r12+18]
       mov       r11,7FF8AF572BF8
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M25_L48
M25_L75:
       mov       rcx,rax
       mov       r11,7FF8AF572B48
       call      qword ptr [r11]
       mov       r12,rax
M25_L76:
       mov       rcx,r12
       mov       r11,7FF8AF572B50
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M25_L48
M25_L77:
       mov       ecx,10C34
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r12,rax
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       r13,rax
       mov       ecx,1CAC
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rcx,r12
       mov       rdx,r13
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rsi,rax
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,r15
       mov       rdx,rsi
       call      qword ptr [7FF8AF966E68]
       mov       rcx,r15
       call      CORINFO_HELP_THROW
       int       3
M25_L78:
       mov       ecx,10A5E
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdi,rax
       mov       rcx,r12
       mov       r11,7FF8AF572B58
       call      qword ptr [r11]
       mov       r14,rax
       mov       ecx,10A98
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rcx,rdi
       mov       rdx,r14
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rbx,rax
       mov       rcx,r12
       mov       r11,7FF8AF572B60
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8B017E1C0]
       int       3
M25_L79:
       mov       rdx,[rax+10]
       mov       rcx,offset MT_Hl7.Fhir.Model.IDynamicType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        near ptr M25_L55
       test      rbx,rbx
       je        near ptr M25_L55
       mov       rcx,[rbx+20]
       test      rcx,rcx
       je        short M25_L80
       mov       rax,[rcx]
       mov       rax,[rax+70]
       call      qword ptr [rax+18]
       test      al,80
       je        near ptr M25_L86
M25_L80:
       mov       rcx,r12
       mov       r11,7FF8AF572C08
       call      qword ptr [r11]
       mov       rcx,rax
       mov       rdx,[r13+8]
       mov       edx,[rdx+8]
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF965620]; System.String.Substring(Int32)
       test      rax,rax
       je        near ptr M25_L55
       cmp       dword ptr [rax+8],0
       jle       near ptr M25_L55
       mov       rcx,[rsi+8]
       mov       rdx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFEAFE88]; Hl7.Fhir.Introspection.ModelInspector.FindClassMapping(System.String)
       mov       r8,rax
       test      r8,r8
       je        near ptr M25_L55
       jmp       near ptr M25_L71
M25_L81:
       mov       rcx,r12
       mov       r11,7FF8AF572C00
       call      qword ptr [r11]
       mov       rcx,rax
       mov       rdx,rcx
       jmp       near ptr M25_L56
M25_L82:
       cmp       qword ptr [rbx+28],0
       setne     r9b
       movzx     r9d,r9b
       mov       eax,1
       jmp       near ptr M25_L67
M25_L83:
       test      r9b,r9b
       jne       short M25_L86
       jmp       near ptr M25_L65
M25_L84:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
M25_L85:
       test      rbx,rbx
       je        near ptr M25_L72
       mov       rcx,[rbx+20]
       test      rcx,rcx
       je        near ptr M25_L72
       mov       rax,[rcx]
       mov       rax,[rax+70]
       call      qword ptr [rax+18]
       test      al,80
       jne       near ptr M25_L72
       cmp       byte ptr [rbx+58],0
       jne       near ptr M25_L72
M25_L86:
       mov       r8,rbx
       jmp       near ptr M25_L71
M25_L87:
       mov       r8,[rbx+20]
       mov       rcx,rsi
       mov       rdx,r12
       call      qword ptr [7FF8B017DDD0]
       mov       r8,rax
       jmp       near ptr M25_L71
M25_L88:
       mov       rcx,[rbp-88]
       mov       r11,7FF8AF572B68
       call      qword ptr [r11]
       mov       rax,r15
       add       rsp,0E8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M25_L89:
       mov       rcx,rbx
       mov       r11,7FF8AF572B20
       call      qword ptr [r11]
       jmp       near ptr M25_L01
M25_L90:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Utility.IAnnotated
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       r13,rax
       jmp       near ptr M25_L02
M25_L91:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Utility.AnnotationList>
       call      CORINFO_HELP_NEWSFAST
       mov       r8,rax
       mov       [rbp-90],r8
       mov       rdx,26D3D803650
       mov       rdx,[rdx]
       mov       rcx,r8
       mov       r8,offset Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D803660
       mov       rdx,[rbp-90]
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,[rbp-90]
       jmp       near ptr M25_L03
M25_L92:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M25_L93:
       mov       rcx,rbx
       mov       r11,7FF8AF572B88
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M25_L25
M25_L94:
       mov       rdx,rbx
       mov       rcx,7FF8B0244A68
       call      qword ptr [7FF8B017DB78]; Hl7.Fhir.ElementModel.TypedElementExtensions.Annotation[[System.__Canon, System.Private.CoreLib]](Hl7.Fhir.ElementModel.ITypedElement)
       test      rax,rax
       jne       short M25_L95
       xor       edx,edx
       jmp       short M25_L96
M25_L95:
       mov       rcx,rax
       mov       r11,7FF8AF572B98
       call      qword ptr [r11]
       mov       rdx,rax
M25_L96:
       test      rdx,rdx
       jne       near ptr M25_L26
       mov       rcx,rdi
       call      qword ptr [7FF8B017DD88]; Hl7.Fhir.Specification.TypeSerializationInfoExtensions.GetTypeName(Hl7.Fhir.Specification.ITypeSerializationInfo)
       mov       rdx,rax
       jmp       near ptr M25_L26
M25_L97:
       mov       rcx,r13
       mov       r11,7FF8AF572B90
       call      qword ptr [r11]
       jmp       near ptr M25_L08
M25_L98:
       mov       rcx,rbx
       mov       r11,7FF8AF572B28
       call      qword ptr [r11]
       mov       r13,rax
       jmp       near ptr M25_L09
M25_L99:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r8,rax
       jmp       near ptr M25_L11
M25_L100:
       mov       rdx,[r8+10]
       mov       rcx,offset MT_Hl7.Fhir.Model.IDynamicType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        near ptr M25_L12
       mov       rdx,r13
       mov       rcx,offset MT_System.String
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r12,rax
       test      r12,r12
       je        near ptr M25_L12
       mov       rcx,rdi
       cmp       [rcx],ecx
       call      qword ptr [7FF8B017E178]
       test      rax,rax
       je        near ptr M25_L12
       mov       rcx,rdi
       call      qword ptr [7FF8B017E178]
       mov       rdx,[rax+28]
       mov       rcx,r12
       call      qword ptr [7FF8B017E190]; Hl7.Fhir.Serialization.PrimitiveTypeConverter.ConvertTo(System.Object, System.Type)
       mov       r12,rax
       jmp       near ptr M25_L13
M25_L101:
       cmp       qword ptr [rdi+28],0
       je        near ptr M25_L16
       mov       rdx,r12
       mov       rcx,offset MT_System.String
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r12,rax
       test      r12,r12
       je        near ptr M25_L16
       mov       rcx,[rdi+28]
       call      qword ptr [7FF8B050EC10]
       mov       rcx,rax
       mov       rdx,r12
       xor       r8d,r8d
       cmp       [rcx],ecx
       call      qword ptr [7FF8B050EC28]
       test      rax,rax
       jne       near ptr M25_L16
       lea       rcx,[rbp-60]
       mov       edx,32
       mov       r8d,2
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,9
       jb        short M25_L102
       mov       rcx,26D0039D6CC
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+2]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+2],xmm1
       mov       ecx,[rbp-50]
       add       ecx,9
       mov       [rbp-50],ecx
       jmp       short M25_L103
M25_L102:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D6C0
       call      qword ptr [7FF8B039CB58]
M25_L103:
       lea       rcx,[rbp-60]
       mov       r8,r13
       mov       rdx,7FF8AF994F18
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,28
       jb        short M25_L104
       mov       rcx,26D0039D824
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+20]
       vmovdqu   xmm2,xmmword ptr [rcx+40]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+20],ymm1
       vmovdqu   xmmword ptr [rdx+40],xmm2
       mov       ecx,[rbp-50]
       add       ecx,28
       mov       [rbp-50],ecx
       jmp       short M25_L105
M25_L104:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D818
       call      qword ptr [7FF8B039CB58]
M25_L105:
       mov       rcx,[rdi+28]
       mov       rax,[rcx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       rdx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       je        short M25_L106
       mov       rcx,26D0039D88C
       movzx     eax,word ptr [rcx]
       mov       [rdx],ax
       mov       ecx,[rbp-50]
       inc       ecx
       mov       [rbp-50],ecx
       jmp       short M25_L107
M25_L106:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D880
       call      qword ptr [7FF8B039CB58]
M25_L107:
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       r13,rax
       mov       rcx,rbx
       mov       r11,7FF8AF572B80
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,r13
       call      qword ptr [7FF8B017E1C0]
       int       3
M25_L108:
       lea       rcx,[rbp-60]
       mov       edx,51
       mov       r8d,3
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       rcx,rbx
       mov       r11,7FF8AF572B70
       call      qword ptr [r11]
       mov       rdx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,18
       jb        short M25_L109
       mov       rcx,26D0039D73C
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+20]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   xmmword ptr [rdx+20],xmm1
       mov       ecx,[rbp-50]
       add       ecx,18
       mov       [rbp-50],ecx
       jmp       short M25_L110
M25_L109:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D730
       call      qword ptr [7FF8B039CB58]
M25_L110:
       mov       rcx,r13
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,1B
       jb        short M25_L111
       mov       rcx,26D0039D784
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+16]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+16],ymm1
       mov       ecx,[rbp-50]
       add       ecx,1B
       mov       [rbp-50],ecx
       jmp       short M25_L112
M25_L111:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D778
       call      qword ptr [7FF8B039CB58]
M25_L112:
       mov       rcx,r15
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,2
       jb        short M25_L113
       mov       rcx,26D002D36B4
       mov       eax,[rcx]
       mov       [rdx],eax
       mov       ecx,[rbp-50]
       add       ecx,2
       mov       [rbp-50],ecx
       jmp       short M25_L114
M25_L113:
       lea       rcx,[rbp-60]
       mov       rdx,26D002D36A8
       call      qword ptr [7FF8B039CB58]
M25_L114:
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        short M25_L117
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,1C
       jb        short M25_L115
       mov       rcx,26D0039D7D4
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+18]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+18],ymm1
       mov       ecx,[rbp-50]
       add       ecx,1C
       mov       [rbp-50],ecx
       jmp       short M25_L116
M25_L115:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039D7C8
       call      qword ptr [7FF8B039CB58]
M25_L116:
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rsi,rax
       mov       rcx,rbx
       mov       r11,7FF8AF572B78
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,rsi
       call      qword ptr [7FF8B017E1C0]
       int       3
M25_L117:
       call      qword ptr [7FF8AF827798]
       int       3
M25_L118:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
M25_L119:
       mov       rcx,r12
       mov       rdx,r13
       call      qword ptr [7FF8AFEAFEA0]
       mov       rcx,rax
       jmp       near ptr M25_L36
M25_L120:
       mov       rcx,r12
       mov       rdx,r13
       mov       r11,7FF8AF572BD0
       call      qword ptr [r11]
       mov       rcx,rax
       jmp       near ptr M25_L36
M25_L121:
       mov       r11,7FF8AF572BD8
       call      qword ptr [r11]
       mov       r12,rax
       jmp       near ptr M25_L37
M25_L122:
       mov       rcx,rax
       mov       r11,7FF8AF572BE0
       call      qword ptr [r11]
       mov       r12,rax
       jmp       near ptr M25_L44
M25_L123:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Specification.IElementDefinitionSummary>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       mov       rcx,r12
       call      qword ptr [7FF8AF9675E8]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor()
       jmp       near ptr M25_L37
M25_L124:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Specification.IElementDefinitionSummary, System.String>
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       rdx,26D3D8035E0
       mov       rdx,[rdx]
       mov       rcx,r13
       mov       r8,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<>c.<Children>b__36_0(Hl7.Fhir.Specification.IElementDefinitionSummary)
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D803610
       mov       rdx,r13
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,r13
       jmp       near ptr M25_L38
M25_L125:
       cmp       qword ptr [rbx+10],0
       je        short M25_L126
       mov       rdx,[rbx+28]
       cmp       dword ptr [rdx+8],0
       jne       short M25_L126
       mov       rdx,[rbx+10]
       mov       rcx,26D0039DAA0
       mov       r8,26D0039D880
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       r13,rax
       mov       rax,[rbx+18]
       mov       [rbp-0F0],rax
       mov       rcx,[rbx+18]
       mov       r11,7FF8AF572BA0
       call      qword ptr [r11]
       mov       rdx,r13
       mov       [rsp+20],rax
       mov       r8,[rbp-0F0]
       mov       rcx,rbx
       xor       r9d,r9d
       call      qword ptr [7FF8B017E010]
M25_L126:
       mov       r8,[rbx+28]
       cmp       dword ptr [r8+8],2
       jne       short M25_L127
       mov       r8,[rbx+18]
       mov       rcx,rbx
       mov       rdx,r12
       xor       r9d,r9d
       call      qword ptr [7FF8B017EDF0]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.enumerateElements(System.Collections.Generic.Dictionary`2<System.String,Hl7.Fhir.Specification.IElementDefinitionSummary>, Hl7.Fhir.ElementModel.ISourceNode, System.String)
       mov       r13,rax
       jmp       near ptr M25_L40
M25_L127:
       mov       rcx,offset MT_System.Array+EmptyArray<Hl7.Fhir.ElementModel.ITypedElement>
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rcx,26D3D8047A0
       mov       r13,[rcx]
       jmp       near ptr M25_L40
M25_L128:
       mov       rcx,rbx
       mov       r11,7FF8AF572B30
       xor       edx,edx
       call      qword ptr [r11]
       mov       rcx,rax
M25_L129:
       mov       r11,7FF8AF572B38
       call      qword ptr [r11]
       mov       rcx,rax
       jmp       near ptr M25_L42
M25_L130:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       mov       rbp,[rcx+28]
       mov       [rsp+28],rbp
       lea       rbp,[rbp+120]
       cmp       qword ptr [rbp-88],0
       je        short M25_L131
       mov       rcx,[rbp-88]
       mov       r11,7FF8AF572B68
       call      qword ptr [r11]
M25_L131:
       nop
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 5399
```
```assembly
; System.Runtime.InteropServices.CollectionsMarshal.SetCount[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.List`1<System.__Canon>, Int32)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rdx
       mov       esi,r8d
       test      esi,esi
       jl        short M26_L05
       inc       dword ptr [rbx+14]
       mov       rcx,[rbx+8]
       cmp       [rcx+8],esi
       jl        short M26_L01
       cmp       esi,[rbx+10]
       jl        short M26_L04
M26_L00:
       mov       [rbx+10],esi
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M26_L01:
       mov       rcx,[rbx+8]
       cmp       dword ptr [rcx+8],0
       jne       short M26_L03
       mov       edx,4
M26_L02:
       mov       ecx,7FFFFFC7
       cmp       edx,7FFFFFC7
       cmova     edx,ecx
       cmp       edx,esi
       cmovl     edx,esi
       mov       rcx,rbx
       call      qword ptr [7FF8AF616F58]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]].set_Capacity(Int32)
       jmp       short M26_L00
M26_L03:
       mov       rdx,[rbx+8]
       mov       edx,[rdx+8]
       add       edx,edx
       jmp       short M26_L02
M26_L04:
       mov       r8d,[rbx+10]
       sub       r8d,esi
       mov       rcx,[rbx+8]
       mov       edx,esi
       call      qword ptr [7FF8AFEA6DF0]; System.Array.Clear(System.Array, Int32, Int32)
       jmp       short M26_L00
M26_L05:
       mov       ecx,253
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B039EB80]
       int       3
; Total bytes of code 150
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AddOrUpdate(System.__Canon, System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,78
       lea       rbp,[rsp+0B0]
       xor       eax,eax
       mov       [rbp-48],rax
       mov       [rbp-50],rax
       mov       [rbp-40],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       r14,r8
       mov       rdi,r9
       test      rsi,rsi
       je        near ptr M27_L15
       test      rdi,rdi
       je        near ptr M27_L14
       mov       r15,[rbx+8]
       mov       r13,[r15+8]
       cmp       byte ptr [rbx+15],0
       jne       near ptr M27_L05
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0C0]
       test      r11,r11
       je        near ptr M27_L04
M27_L00:
       mov       rcx,r13
       mov       rdx,rsi
       call      qword ptr [r11]
       mov       r12d,eax
M27_L01:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+0B0]
       test      rax,rax
       je        near ptr M27_L06
M27_L02:
       mov       [rbp-60],rax
M27_L03:
       mov       rcx,rax
       lea       rdx,[rbp-48]
       mov       [rsp+20],rdx
       mov       rdx,r15
       mov       r8,rsi
       mov       r9d,r12d
       call      qword ptr [7FF8AF9675D0]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       jne       near ptr M27_L07
       mov       byte ptr [rbp-58],1
       mov       [rbp-54],r12d
       mov       [rsp+20],r14
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-50]
       mov       [rsp+38],r9
       mov       r9,[rbp-58]
       mov       rdx,r15
       mov       r8,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AF96DEA8]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       test      eax,eax
       je        near ptr M27_L08
       mov       rax,[rbp-50]
       add       rsp,78
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M27_L04:
       mov       rdx,7FF8B0439B40
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       near ptr M27_L00
M27_L05:
       mov       rcx,rsi
       mov       rax,[rsi]
       mov       rax,[rax+40]
       call      qword ptr [rax+18]
       mov       r12d,eax
       jmp       near ptr M27_L01
M27_L06:
       mov       rdx,7FF8B0439B10
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       near ptr M27_L02
M27_L07:
       mov       rdx,rsi
       mov       r8,[rbp-48]
       mov       rcx,[rdi+8]
       call      qword ptr [rdi+18]
       mov       byte ptr [rbp-58],1
       mov       [rbp-54],r12d
       mov       [rbp-68],rax
       mov       [rsp+20],rax
       mov       r9,[rbp-48]
       mov       [rsp+28],r9
       mov       r9,[rbp-58]
       mov       rdx,r15
       mov       r8,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8B05066E8]
       test      eax,eax
       jne       short M27_L13
M27_L08:
       cmp       r15,[rbx+8]
       mov       rax,[rbp-60]
       je        near ptr M27_L03
       mov       r15,[rbx+8]
       cmp       r13,[r15+8]
       je        near ptr M27_L03
       mov       r13,[r15+8]
       cmp       byte ptr [rbx+15],0
       jne       short M27_L11
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+0C0]
       test      r11,r11
       je        short M27_L09
       jmp       short M27_L10
M27_L09:
       mov       rdx,7FF8B0439B40
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M27_L10:
       mov       rcx,r13
       mov       rdx,rsi
       call      qword ptr [r11]
       mov       r12d,eax
       jmp       short M27_L12
M27_L11:
       mov       rcx,rsi
       mov       rdx,[rsi]
       mov       rdx,[rdx+40]
       call      qword ptr [rdx+18]
       mov       r12d,eax
M27_L12:
       mov       rax,[rbp-60]
       jmp       near ptr M27_L03
M27_L13:
       mov       rsi,[rbp-68]
       mov       rax,rsi
       add       rsp,78
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M27_L14:
       mov       ecx,0C3A
       mov       rdx,7FF8AF997C50
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8AFB172A0]
       int       3
M27_L15:
       mov       ecx,1
       mov       rdx,7FF8AF997C50
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8AFB172A0]
       int       3
; Total bytes of code 605
```
```assembly
; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rdx
       mov       rcx,offset Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       cmp       [r8+18],rcx
       jne       near ptr M28_L02
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,26D3D803678
       mov       rdi,[rcx]
       test      rdi,rdi
       je        near ptr M28_L04
M28_L00:
       mov       rcx,offset MT_System.Lazy<System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       test      rdi,rdi
       je        near ptr M28_L05
       lea       rcx,[rbp+10]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset MT_System.LazyHelper
       call      CORINFO_HELP_NEWSFAST
       mov       dword ptr [rax+10],8
       lea       rcx,[rbp+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rsi+8]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
M28_L01:
       test      rsi,rsi
       je        near ptr M28_L07
       test      rbx,rbx
       jne       short M28_L03
       jmp       near ptr M28_L06
M28_L02:
       mov       rcx,[r8+8]
       call      qword ptr [r8+18]
       mov       rsi,rax
       jmp       short M28_L01
M28_L03:
       mov       rcx,rbx
       mov       rdx,rsi
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       rax,[rbx]
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M28_L04:
       mov       rcx,offset MT_System.Func<System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>>
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       rdx,26D3D803668
       mov       rdx,[rdx]
       mov       rcx,rdi
       mov       r8,offset Hl7.Fhir.Utility.AnnotationList+<>c.<.ctor>b__13_0()
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D803678
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M28_L00
M28_L05:
       mov       ecx,23B3
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B0506550]
       int       3
M28_L06:
       call      qword ptr [7FF8B05064C0]
       int       3
M28_L07:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B05064D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 369
```
```assembly
; Hl7.Fhir.Model.PrimitiveNode..ctor(Hl7.Fhir.Model.PrimitiveType, Hl7.Fhir.Model.PocoNodeOrList, System.Nullable`1<Int32>, System.String)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       rbp,r9
       lea       rcx,[rbx+30]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+10]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+18]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+28],rbp
       mov       rdi,[rsp+70]
       mov       rdx,rdi
       test      rdx,rdx
       je        short M29_L01
M29_L00:
       lea       rcx,[rbx+8]
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M29_L01:
       mov       rcx,offset MT_Hl7.Fhir.Model.Integer
       cmp       [rsi],rcx
       jne       short M29_L03
       mov       rdx,26D002DD7C0
M29_L02:
       jmp       short M29_L00
M29_L03:
       mov       rcx,rsi
       mov       rax,[rsi]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       rdx,rax
       jmp       short M29_L02
; Total bytes of code 137
```
```assembly
; Hl7.Fhir.Model.PocoNode+<>c.<get_Annotations>b__58_0()
       push      rbx
       sub       rsp,20
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8B017EBB0]; Hl7.Fhir.Utility.AnnotationList..ctor()
       mov       rax,rbx
       add       rsp,20
       pop       rbx
       ret
; Total bytes of code 41
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,r8
       test      rdx,rdx
       je        short M31_L00
       lea       rcx,[rbx+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rsi
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M31_L00:
       call      qword ptr [7FF8B045E778]
       int       3
; Total bytes of code 44
```
```assembly
; System.Linq.Enumerable.Count[[System.Collections.Generic.KeyValuePair`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]], System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>)
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+50]
       mov       [rbp-30],rsp
       mov       [rbp-20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M32_L14
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+10]
       test      rcx,rcx
       je        short M32_L02
M32_L00:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rdi,rax
       test      rdi,rdi
       je        short M32_L06
       mov       rcx,[rbx+18]
       mov       r11,[rcx+28]
       test      r11,r11
       je        short M32_L03
M32_L01:
       mov       rcx,rdi
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       jmp       qword ptr [r11]
M32_L02:
       mov       rcx,rbx
       mov       rdx,7FF8B03B36D8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M32_L00
M32_L03:
       mov       rcx,rbx
       mov       rdx,7FF8B03B3938
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M32_L01
M32_L04:
       mov       rcx,[rbp-28]
       mov       r11,7FF8AF573238
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M32_L11
       add       edi,1
       jo        short M32_L05
       jmp       short M32_L04
M32_L05:
       call      CORINFO_HELP_OVERFLOW
       int       3
M32_L06:
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+18]
       test      rcx,rcx
       je        short M32_L07
       jmp       short M32_L08
M32_L07:
       mov       rcx,rbx
       mov       rdx,7FF8B03B37F8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
M32_L08:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M32_L13
       mov       rdx,rsi
       mov       rcx,offset MT_System.Collections.ICollection
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       jne       short M32_L12
       xor       edi,edi
       mov       rcx,[rbx+18]
       mov       r11,[rcx+20]
       test      r11,r11
       je        short M32_L09
       jmp       short M32_L10
M32_L09:
       mov       rcx,rbx
       mov       rdx,7FF8B03B3920
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
M32_L10:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       [rbp-28],rax
       jmp       near ptr M32_L04
M32_L11:
       mov       rcx,[rbp-28]
       mov       r11,7FF8AF573240
       call      qword ptr [r11]
       mov       eax,edi
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M32_L12:
       mov       rcx,rax
       mov       r11,7FF8AF573248
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       jmp       qword ptr [r11]
M32_L13:
       mov       rcx,rax
       xor       edx,edx
       mov       rax,[rax]
       mov       rax,[rax+40]
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       jmp       qword ptr [rax+30]
M32_L14:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+50]
       cmp       qword ptr [rbp-28],0
       je        short M32_L15
       mov       rcx,[rbp-28]
       mov       r11,7FF8AF573240
       call      qword ptr [r11]
M32_L15:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
; Total bytes of code 448
```
```assembly
; System.DateTime.UpdateLeapSecondCacheAndReturnUtcNow()
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+80]
       call      qword ptr [7FF90B371258]
       mov       rax,[rax]
       lea       rcx,[rbp-20]
       call      rax
       mov       rdx,346DC5D63886594B
       mov       rax,rdx
       mul       qword ptr [rbp-20]
       shr       rdx,0B
       imul      rcx,rdx,2710
       mov       rbx,[rbp-20]
       sub       rbx,rcx
       mov       rcx,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       cmp       dword ptr [rcx],0
       jne       near ptr M33_L04
M33_L00:
       lea       rcx,[rbp-20]
       lea       rdx,[rbp-30]
       call      qword ptr [7FF90B39D4A0]
       test      eax,eax
       je        near ptr M33_L08
       cmp       word ptr [rbp-24],3C
       jae       near ptr M33_L07
       mov       ecx,0B2D05E00
       add       rcx,[rbp-20]
       mov       [rbp-38],rcx
       mov       rcx,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       cmp       dword ptr [rcx],0
       jne       short M33_L05
M33_L01:
       lea       rcx,[rbp-38]
       lea       rdx,[rbp-48]
       call      qword ptr [7FF90B39D4A0]
       test      eax,eax
       je        near ptr M33_L08
       movzx     ecx,word ptr [rbp-3C]
       cmp       cx,[rbp-24]
       jne       short M33_L06
       mov       rsi,[rbp-20]
       lea       rcx,[rbp-30]
       mov       rdx,rbx
       call      qword ptr [7FF90B383BB0]
       mov       rbx,rax
M33_L02:
       call      qword ptr [7FF90B37AEA0]
       mov       rdi,rax
       call      qword ptr [7FF90B3714B0]
       mov       [rdi+8],rsi
       mov       [rdi+10],rbx
       mov       rcx,rax
       mov       rdx,rdi
       call      qword ptr [7FF90B3710F8]; CORINFO_HELP_CHECKED_ASSIGN_REF
       add       rbx,[rbp-20]
       sub       rbx,rsi
       mov       rax,rbx
M33_L03:
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M33_L04:
       call      qword ptr [7FF90B371148]; CORINFO_HELP_POLL_GC
       jmp       near ptr M33_L00
M33_L05:
       call      qword ptr [7FF90B371148]; CORINFO_HELP_POLL_GC
       jmp       short M33_L01
M33_L06:
       movups    xmm0,[rbp-30]
       movups    [rbp-58],xmm0
       mov       word ptr [rbp-50],0
       mov       word ptr [rbp-4E],0
       mov       word ptr [rbp-4C],0
       mov       word ptr [rbp-4A],0
       lea       rcx,[rbp-58]
       lea       rdx,[rbp-60]
       call      qword ptr [7FF90B3812A0]
       test      eax,eax
       je        short M33_L08
       mov       rsi,0C87700CB80
       add       rsi,[rbp-60]
       mov       rcx,[rbp-20]
       sub       rcx,rsi
       mov       edx,0B2D05E00
       cmp       rcx,rdx
       jae       short M33_L07
       lea       rcx,[rbp-58]
       xor       edx,edx
       call      qword ptr [7FF90B383BB0]
       mov       rbx,0C87700CB80
       add       rbx,rax
       jmp       near ptr M33_L02
M33_L07:
       lea       rcx,[rbp-30]
       mov       rdx,rbx
       call      qword ptr [7FF90B383BB0]
       jmp       near ptr M33_L03
M33_L08:
       call      qword ptr [7FF90B383BD0]
       jmp       near ptr M33_L03
; Total bytes of code 405
```
```assembly
; System.TimeZoneInfo.GetUtcOffset(System.DateTime, System.TimeZoneInfoOptions, CachedData)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       mov       rbx,rcx
       mov       rdi,rdx
       mov       ebp,r8d
       mov       rsi,r9
       mov       r8,0C000000000000000
       and       r8,rdi
       mov       r9,r8
       test      r9,r9
       jne       short M34_L01
M34_L00:
       test      r8,r8
       je        near ptr M34_L05
       jmp       short M34_L02
M34_L01:
       mov       rcx,4000000000000000
       cmp       r9,rcx
       je        short M34_L00
       jmp       short M34_L04
M34_L02:
       mov       r9,4000000000000000
       cmp       r8,r9
       jne       near ptr M34_L05
       cmp       [rsi],sil
       mov       r8,26D3D804270
       cmp       rbx,[r8]
       je        short M34_L03
       lea       r8,[rsp+30]
       lea       r9,[rsp+28]
       mov       rcx,rdi
       mov       rdx,rbx
       call      qword ptr [7FF8B0395FF8]; System.TimeZoneInfo.GetUtcOffsetFromUtc(System.DateTime, System.TimeZoneInfo, Boolean ByRef, Boolean ByRef)
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M34_L03:
       mov       rax,[rbx+40]
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M34_L04:
       mov       rcx,rsi
       mov       rdx,rbx
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0395FE0]; System.TimeZoneInfo+CachedData.GetCorrespondingKind(System.TimeZoneInfo)
       cmp       eax,2
       je        short M34_L05
       mov       rcx,rsi
       call      qword ptr [7FF8B050D248]
       mov       rdx,rax
       mov       r8,26D3D804278
       mov       r8,[r8]
       mov       [rsp+20],r8
       mov       r8,26D3D804270
       mov       r8,[r8]
       mov       rcx,rdi
       mov       r9d,ebp
       call      qword ptr [7FF8B050D260]
       mov       rcx,rax
       lea       r8,[rsp+40]
       lea       r9,[rsp+38]
       mov       rdx,rbx
       call      qword ptr [7FF8B0395FF8]; System.TimeZoneInfo.GetUtcOffsetFromUtc(System.DateTime, System.TimeZoneInfo, Boolean ByRef, Boolean ByRef)
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M34_L05:
       mov       rcx,rdi
       mov       rdx,rbx
       call      qword ptr [7FF8B050D230]
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 279
```
```assembly
; System.TimeZoneInfo+CachedData.CreateLocal()
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+0A0]
       mov       [rbp-58],rsp
       mov       [rbp+10],rcx
       xor       edx,edx
       mov       [rbp-40],edx
       cmp       byte ptr [rbp-40],0
       jne       near ptr M35_L00
       lea       rdx,[rbp-40]
       call      qword ptr [7FF90B388EC0]; System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
       mov       rcx,[rbp+10]
       mov       rbx,[rcx+8]
       test      rbx,rbx
       jne       near ptr M35_L01
       call      qword ptr [7FF90B386B98]
       mov       rbx,rax
       mov       rsi,[rbx+8]
       mov       rdi,[rbx+40]
       mov       rcx,rbx
       call      qword ptr [7FF90B3869C8]
       mov       r14,rax
       mov       rcx,rbx
       call      qword ptr [7FF90B3869D8]
       mov       r15,rax
       mov       rcx,rbx
       call      qword ptr [7FF90B3869E0]
       mov       r13,rax
       mov       r12,[rbx+28]
       call      qword ptr [7FF90B37B0A0]
       mov       [rbp-48],rax
       mov       [rsp+20],r15
       mov       [rsp+28],r13
       mov       [rsp+30],r12
       xor       ecx,ecx
       mov       [rsp+38],ecx
       movzx     ecx,byte ptr [rbx+39]
       mov       [rsp+40],ecx
       mov       rcx,rax
       mov       rdx,rsi
       mov       r8,rdi
       mov       r9,r14
       call      qword ptr [7FF90B386A68]; Precode of System.TimeZoneInfo..ctor(System.String, System.TimeSpan, System.String, System.String, System.String, AdjustmentRule[], Boolean, Boolean)
       mov       rbx,[rbp-48]
       mov       rcx,[rbp+10]
       lea       rcx,[rcx+8]
       mov       rdx,rbx
       call      qword ptr [7FF90B3710F0]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+10]
       jmp       short M35_L01
M35_L00:
       call      qword ptr [7FF90B388EB8]
       int       3
M35_L01:
       cmp       byte ptr [rbp-40],0
       je        short M35_L02
       call      qword ptr [7FF90B388ED0]; System.Threading.Monitor.Exit(System.Object)
M35_L02:
       mov       rax,rbx
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,58
       mov       rbp,[rcx+48]
       mov       [rsp+48],rbp
       lea       rbp,[rbp+0A0]
       cmp       byte ptr [rbp-40],0
       je        short M35_L03
       mov       rcx,[rbp+10]
       call      qword ptr [7FF90B388ED0]; System.Threading.Monitor.Exit(System.Object)
M35_L03:
       nop
       add       rsp,58
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 320
```
```assembly
; System.Collections.Generic.EqualityComparer`1[[System.__Canon, System.Private.CoreLib]].get_Default()
       sub       rsp,28
       mov       [rsp+20],rcx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,[rax]
       add       rsp,28
       ret
; Total bytes of code 22
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       push      rsi
       push      rbx
       test      rdx,rdx
       je        short M37_L01
       mov       r8,[rdx]
       cmp       r8,rcx
       je        short M37_L01
       mov       rax,26D3D800038
       mov       r10,[rax]
       add       r10,10
       rorx      rax,r8,20
       xor       rax,rcx
       mov       r9,9E3779B97F4A7C15
       imul      rax,r9
       mov       r9d,[r10]
       shrx      r9,rax,r9
       xor       r11d,r11d
M37_L00:
       lea       eax,[r9+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[r10+rax*8]
       mov       ebx,[rax]
       mov       rsi,[rax+8]
       and       ebx,0FFFFFFFE
       cmp       rsi,r8
       jne       short M37_L02
       mov       rsi,rcx
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M37_L02
       cmp       ebx,[rax]
       jne       short M37_L03
       cmp       esi,1
       jne       short M37_L03
M37_L01:
       mov       rax,rdx
       pop       rbx
       pop       rsi
       ret
M37_L02:
       test      ebx,ebx
       je        short M37_L03
       inc       r11d
       add       r9d,r11d
       and       r9d,[r10+4]
       cmp       r11d,8
       jl        short M37_L00
M37_L03:
       pop       rbx
       pop       rsi
       jmp       near ptr System.Runtime.CompilerServices.CastHelpers.ChkCastAny_NoCacheLookup(Void*, System.Object)
; Total bytes of code 149
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Initialize(Int32)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       ecx,edx
       call      qword ptr [7FF8AF615938]; System.Collections.HashHelpers.GetPrime(Int32)
       mov       esi,eax
       movsxd    rdx,esi
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       mov       rdi,rax
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+90]
       test      rax,rax
       je        short M38_L01
       mov       rcx,rax
M38_L00:
       movsxd    rdx,esi
       call      CORINFO_HELP_NEWARR_1_VC
       mov       rbp,rax
       mov       dword ptr [rbx+3C],0FFFFFFFF
       mov       rax,0FFFFFFFFFFFFFFFF
       mov       ecx,esi
       xor       edx,edx
       div       rcx
       inc       rax
       mov       [rbx+30],rax
       lea       rcx,[rbx+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+10]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       eax,esi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M38_L01:
       mov       rdx,7FF8B0590C98
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       short M38_L00
; Total bytes of code 169
```
```assembly
; Hl7.Fhir.Model.PocoNodeExtensions.GetResourceContext(Hl7.Fhir.Model.PocoNodeOrList)
M39_L00:
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       xor       eax,eax
       mov       [rsp+20],rax
       mov       rbx,rcx
       mov       rcx,rbx
       test      rcx,rcx
       je        short M39_L01
       mov       rdx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [rcx],rdx
       jne       short M39_L03
       xor       ecx,ecx
M39_L01:
       test      rcx,rcx
       jne       near ptr M39_L11
       mov       rsi,rbx
       test      rsi,rsi
       je        short M39_L02
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [rsi],rcx
       jne       short M39_L04
M39_L02:
       test      rsi,rsi
       je        near ptr M39_L12
       mov       rdx,offset MT_Hl7.Fhir.Model.PocoNode
       cmp       [rsi],rdx
       jne       near ptr M39_L08
       mov       rdi,[rsi+18]
       mov       rdx,rdi
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoListNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rbp,rax
       test      rbp,rbp
       jne       short M39_L06
       jmp       short M39_L05
M39_L03:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoListNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       short M39_L01
M39_L04:
       mov       rdx,rbx
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rsi,rax
       jmp       short M39_L02
M39_L05:
       mov       rdx,rdi
       mov       rcx,offset MT_Hl7.Fhir.Model.PocoNode
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       xor       ecx,ecx
       test      rax,rax
       cmove     rax,rcx
       jmp       short M39_L07
M39_L06:
       mov       rcx,[rsi+28]
       mov       [rsp+20],rcx
       lea       rcx,[rsp+20]
       call      qword ptr [7FF8B0396520]; System.Nullable`1[[System.Int32, System.Private.CoreLib]].get_Value()
       mov       edx,eax
       mov       rcx,rbp
       call      qword ptr [7FF8B0396538]
M39_L07:
       jmp       short M39_L09
M39_L08:
       mov       rcx,rsi
       mov       rax,[rsi]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
M39_L09:
       test      rax,rax
       je        short M39_L10
       mov       rdx,[rsi+10]
       mov       rcx,offset MT_Hl7.Fhir.Model.Resource
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M39_L12
M39_L10:
       jmp       short M39_L17
M39_L11:
       mov       rax,[rcx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       test      rax,rax
       je        short M39_L16
M39_L12:
       test      rbx,rbx
       je        short M39_L13
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       test      rax,rax
       jne       short M39_L14
M39_L13:
       xor       esi,esi
       jmp       short M39_L15
M39_L14:
       mov       rcx,rax
       call      qword ptr [7FF8B0396388]
       mov       rsi,rax
M39_L15:
       jmp       short M39_L17
M39_L16:
       xor       esi,esi
M39_L17:
       mov       rax,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 347
```
```assembly
; Hl7.FhirPath.Expressions.Closure.SetValue(System.String, System.Collections.Generic.IEnumerable`1<Hl7.Fhir.Model.PocoNode>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       rcx,[rbx+18]
       mov       rdx,rsi
       cmp       [rcx],ecx
       call      qword ptr [7FF8AF686538]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       mov       rcx,[rbx+18]
       cmp       [rcx],cl
       mov       rdx,rsi
       mov       r8,rdi
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       nop
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 64
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Remove(System.__Canon)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       mov       [rsp+30],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M41_L12
       cmp       qword ptr [rbx+8],0
       je        short M41_L01
       xor       edi,edi
       mov       rbp,[rbx+18]
       mov       rcx,[rbx]
       mov       r11,[rcx+30]
       mov       r11,[r11]
       mov       r11,[r11+68]
       test      r11,r11
       je        short M41_L02
M41_L00:
       mov       rcx,rbp
       mov       rdx,rsi
       call      qword ptr [r11]
       mov       r14d,eax
       mov       rax,[rbx+8]
       mov       ecx,r14d
       imul      rcx,[rbx+30]
       shr       rcx,20
       inc       rcx
       mov       edx,[rax+8]
       mov       r8d,edx
       imul      rcx,r8
       shr       rcx,20
       cmp       ecx,edx
       jae       near ptr M41_L13
       mov       ecx,ecx
       lea       r15,[rax+rcx*4+10]
       mov       r13,[rbx+10]
       mov       r12d,0FFFFFFFF
       mov       r8d,[r15]
       dec       r8d
       jns       short M41_L03
M41_L01:
       xor       eax,eax
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M41_L02:
       mov       rdx,7FF8B04374B8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M41_L00
M41_L03:
       mov       eax,[r13+8]
       mov       [rsp+28],eax
       cmp       r8d,eax
       jae       near ptr M41_L13
       mov       ecx,r8d
       lea       rcx,[rcx+rcx*2]
       lea       r10,[r13+rcx*8+10]
       mov       [rsp+20],r10
       cmp       [r10+10],r14d
       je        short M41_L05
M41_L04:
       mov       r12d,r8d
       mov       r8d,[r10+14]
       inc       edi
       cmp       eax,edi
       jb        short M41_L08
       mov       [rsp+2C],r8d
       test      r8d,r8d
       mov       r8d,[rsp+2C]
       jge       short M41_L03
       jmp       short M41_L01
M41_L05:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+70]
       test      r11,r11
       je        short M41_L06
       mov       [rsp+2C],r8d
       jmp       short M41_L07
M41_L06:
       mov       [rsp+2C],r8d
       mov       rdx,7FF8B04374D0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       mov       r10,[rsp+20]
M41_L07:
       mov       rdx,[r10]
       mov       rcx,rbp
       mov       r8,rsi
       call      qword ptr [r11]
       test      eax,eax
       mov       eax,[rsp+28]
       mov       r8d,[rsp+2C]
       mov       r10,[rsp+20]
       je        short M41_L04
       jmp       short M41_L09
M41_L08:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M41_L09:
       test      r12d,r12d
       jge       short M41_L10
       mov       eax,[r10+14]
       inc       eax
       mov       [r15],eax
       jmp       short M41_L11
M41_L10:
       cmp       r12d,eax
       jae       short M41_L13
       mov       eax,r12d
       lea       rax,[rax+rax*2]
       mov       ecx,[r10+14]
       mov       [r13+rax*8+24],ecx
M41_L11:
       mov       eax,[rbx+3C]
       neg       eax
       add       eax,0FFFFFFFD
       mov       [r10+14],eax
       xor       eax,eax
       mov       [r10],rax
       mov       [r10+8],rax
       mov       [rbx+3C],r8d
       inc       dword ptr [rbx+40]
       mov       eax,1
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M41_L12:
       mov       ecx,4
       call      qword ptr [7FF8AF61FB28]
       int       3
M41_L13:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 453
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       mov       [rsp+40],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       ebp,r9d
       test      rsi,rsi
       je        near ptr M42_L22
       cmp       qword ptr [rbx+8],0
       je        near ptr M42_L06
M42_L00:
       mov       r14,[rbx+10]
       mov       r15,[rbx+18]
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+68]
       test      r11,r11
       je        near ptr M42_L07
M42_L01:
       mov       rcx,r15
       mov       rdx,rsi
       call      qword ptr [r11]
       mov       r13d,eax
       xor       r12d,r12d
       mov       rcx,[rbx+8]
       mov       edx,r13d
       imul      rdx,[rbx+30]
       shr       rdx,20
       inc       rdx
       mov       eax,[rcx+8]
       mov       r8d,eax
       imul      rdx,r8
       shr       rdx,20
       cmp       edx,eax
       jae       near ptr M42_L23
       mov       edx,edx
       lea       rax,[rcx+rdx*4+10]
       mov       r8d,[rax]
       dec       r8d
       mov       r10d,[r14+8]
       mov       [rsp+38],r10d
       cmp       r10d,r8d
       ja        near ptr M42_L08
M42_L02:
       cmp       dword ptr [rbx+40],0
       jg        near ptr M42_L11
       mov       ebp,[rbx+38]
       cmp       r10d,ebp
       je        near ptr M42_L10
M42_L03:
       lea       ecx,[rbp+1]
       mov       [rbx+38],ecx
       mov       r14,[rbx+10]
M42_L04:
       cmp       ebp,[r14+8]
       jae       near ptr M42_L23
       mov       ecx,ebp
       lea       rcx,[rcx+rcx*2]
       lea       r8,[r14+rcx*8+10]
       mov       [rsp+28],r8
       mov       [r8+10],r13d
       mov       [rsp+30],rax
       mov       ecx,[rax]
       dec       ecx
       mov       [r8+14],ecx
       mov       rcx,r8
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       r13,[rsp+28]
       lea       rcx,[r13+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       inc       ebp
       mov       rdi,[rsp+30]
       mov       [rdi],ebp
       inc       dword ptr [rbx+44]
       cmp       r12d,64
       ja        near ptr M42_L15
M42_L05:
       mov       eax,1
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M42_L06:
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FF8AF615920]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Initialize(Int32)
       jmp       near ptr M42_L00
M42_L07:
       mov       rdx,7FF8B04374B8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       near ptr M42_L01
M42_L08:
       mov       ecx,r8d
       lea       rcx,[rcx+rcx*2]
       lea       rdx,[r14+rcx*8+10]
       mov       [rsp+20],rdx
       cmp       [rdx+10],r13d
       je        near ptr M42_L12
M42_L09:
       mov       r8d,[rdx+14]
       inc       r12d
       cmp       r10d,r12d
       jb        near ptr M42_L16
       cmp       r10d,r8d
       ja        short M42_L08
       jmp       near ptr M42_L02
M42_L10:
       mov       rcx,rbx
       call      qword ptr [7FF8B0504378]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Resize()
       mov       rcx,[rbx+8]
       mov       edx,r13d
       imul      rdx,[rbx+30]
       shr       rdx,20
       inc       rdx
       mov       eax,[rcx+8]
       mov       r8d,eax
       imul      rdx,r8
       shr       rdx,20
       cmp       edx,eax
       jae       near ptr M42_L23
       mov       edx,edx
       lea       rax,[rcx+rdx*4+10]
       mov       [rsp+30],rax
       mov       rax,[rsp+30]
       jmp       near ptr M42_L03
M42_L11:
       mov       ecx,[rbx+3C]
       mov       ebp,ecx
       cmp       ecx,r10d
       jae       near ptr M42_L23
       lea       rcx,[rcx+rcx*2]
       mov       ecx,[r14+rcx*8+24]
       neg       ecx
       add       ecx,0FFFFFFFD
       mov       [rbx+3C],ecx
       dec       dword ptr [rbx+40]
       jmp       near ptr M42_L04
M42_L12:
       mov       rcx,[rbx]
       mov       r9,[rcx+30]
       mov       r9,[r9]
       mov       r11,[r9+70]
       test      r11,r11
       je        short M42_L13
       mov       [rsp+30],rax
       jmp       short M42_L14
M42_L13:
       mov       [rsp+30],rax
       mov       [rsp+3C],r8d
       mov       rdx,7FF8B04374D0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       mov       r8d,[rsp+3C]
M42_L14:
       mov       ecx,r8d
       lea       rcx,[rcx+rcx*2]
       mov       rdx,[r14+rcx*8+10]
       mov       rcx,r15
       mov       r8,rsi
       call      qword ptr [r11]
       test      eax,eax
       mov       rax,[rsp+30]
       mov       rdx,[rsp+20]
       mov       r10d,[rsp+38]
       je        near ptr M42_L09
       jmp       short M42_L17
M42_L15:
       mov       rdx,r15
       mov       rcx,offset MT_System.Collections.Generic.NonRandomizedStringEqualityComparer
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        near ptr M42_L05
       mov       edx,[r14+8]
       mov       rcx,rbx
       mov       r8d,1
       call      qword ptr [7FF8AF967210]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Resize(Int32, Boolean)
       jmp       near ptr M42_L05
M42_L16:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M42_L17:
       cmp       bpl,1
       je        short M42_L21
       cmp       bpl,2
       jne       short M42_L20
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+78]
       test      rax,rax
       je        short M42_L18
       mov       rcx,rax
       jmp       short M42_L19
M42_L18:
       mov       rdx,7FF8B04374E8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
M42_L19:
       mov       rdx,rsi
       call      qword ptr [7FF8AF61FAF8]
       int       3
M42_L20:
       xor       eax,eax
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M42_L21:
       lea       rcx,[rdx+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       eax,1
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M42_L22:
       mov       ecx,4
       call      qword ptr [7FF8AF61FB28]
       int       3
M42_L23:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 819
```
```assembly
; System.RuntimeType.CreateInstanceOfT()
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       lea       rbp,[rsp+40]
       mov       [rbp-20],rsp
       mov       rbx,rcx
       mov       rcx,[rbx+10]
       test      rcx,rcx
       je        short M43_L02
       mov       rax,[rcx]
       test      rax,rax
       je        short M43_L02
M43_L00:
       mov       rsi,[rax+78]
       test      rsi,rsi
       je        short M43_L04
       mov       rcx,offset MT_System.RuntimeType+ActivatorCache
       cmp       [rsi],rcx
       jne       short M43_L03
M43_L01:
       cmp       byte ptr [rsi+28],0
       je        short M43_L06
       mov       rax,[rsi+8]
       mov       rcx,[rsi+10]
       call      rax
       mov       rdi,rax
       jmp       short M43_L05
M43_L02:
       mov       rcx,rbx
       call      qword ptr [7FF8AF82C498]; System.RuntimeType.InitializeCache()
       jmp       short M43_L00
M43_L03:
       mov       rdx,offset MT_System.RuntimeType+CompositeCacheEntry
       cmp       [rsi],rdx
       jne       short M43_L04
       mov       rdx,rsi
       mov       rsi,[rdx+8]
       test      rsi,rsi
       je        short M43_L04
       jmp       short M43_L01
M43_L04:
       mov       rdx,rbx
       mov       rcx,offset MT_System.RuntimeType+IGenericCacheEntry<System.RuntimeType+ActivatorCache>
       call      qword ptr [7FF8AF82FB40]; System.RuntimeType+IGenericCacheEntry`1[[System.__Canon, System.Private.CoreLib]].CreateAndCache(System.RuntimeType)
       mov       rsi,rax
       jmp       short M43_L01
M43_L05:
       mov       rax,[rsi+18]
       mov       rcx,rdi
       call      rax
       nop
       mov       rax,rdi
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M43_L06:
       mov       rcx,offset MT_System.MissingMethodException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       call      qword ptr [7FF8B050D3B0]
       mov       rcx,rax
       mov       rdx,rbx
       call      qword ptr [7FF8B045EA90]
       mov       rdx,rax
       mov       rcx,rsi
       call      qword ptr [7FF8B050D3C8]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+40]
       mov       rbx,rdx
       mov       rcx,offset MT_System.Reflection.TargetInvocationException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8B050D3E0]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 288
```
```assembly
; Hl7.FhirPath.EvaluationContext..ctor()
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rcx,offset MT_System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.IEnumerable<Hl7.Fhir.Model.PocoNode>>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       xor       edx,edx
       xor       r8d,r8d
       call      qword ptr [7FF8AF615908]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       lea       rcx,[rbx+18]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 61
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetEnumerator()
       push      rdi
       push      rsi
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+8],rax
       xorps     xmm4,xmm4
       movaps    [rsp+10],xmm4
       movaps    [rsp+20],xmm4
       mov       [rsp+30],rcx
       mov       r8,rcx
       cmp       [r8],r8d
       mov       r10d,[r8+44]
       mov       [rsp+8],r8
       mov       rdi,rdx
       lea       rsi,[rsp+8]
       call      qword ptr [7FF90B371100]; CORINFO_HELP_ASSIGN_BYREF
       movsq
       movsq
       call      qword ptr [7FF90B371100]; CORINFO_HELP_ASSIGN_BYREF
       call      qword ptr [7FF90B371100]; CORINFO_HELP_ASSIGN_BYREF
       mov       [rdx+8],r10d
       xor       eax,eax
       mov       [rdx+0C],eax
       mov       dword ptr [rdx+10],2
       mov       rax,rdx
       add       rsp,38
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 102
```
```assembly
; Hl7.FhirPath.Expressions.Closure.get_focus()
       cmp       byte ptr [rcx+2C],0
       jne       short M46_L00
       mov       rax,26D3D8042D0
       mov       rax,[rax]
       ret
M46_L00:
       mov       rax,[rcx+8]
       ret
; Total bytes of code 25
```
```assembly
; System.Linq.Enumerable.TryGetFirst[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        near ptr M47_L08
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+20]
       test      rcx,rcx
       je        short M47_L02
M47_L00:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rbp,rax
       test      rbp,rbp
       jne       short M47_L04
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+28]
       test      rcx,rcx
       je        short M47_L03
M47_L01:
       mov       rdx,rsi
       mov       r8,rdi
       call      qword ptr [7FF8AFEAFE10]; System.Linq.Enumerable.TryGetFirstNonIterator[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
       nop
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M47_L02:
       mov       rcx,rbx
       mov       rdx,7FF8B0439278
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M47_L00
M47_L03:
       mov       rcx,rbx
       mov       rdx,7FF8B0439470
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M47_L01
M47_L04:
       mov       rcx,offset MT_System.Linq.Enumerable+IListSkipTakeIterator<Newtonsoft.Json.Linq.JProperty>
       cmp       [rbp],rcx
       jne       short M47_L07
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF5727D0
       call      qword ptr [r11]
       cmp       eax,[rbp+20]
       jg        short M47_L06
       mov       byte ptr [rdi],0
       xor       eax,eax
M47_L05:
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M47_L06:
       mov       byte ptr [rdi],1
       mov       rcx,[rbp+18]
       mov       edx,[rbp+20]
       mov       r11,7FF8AF5727D8
       call      qword ptr [r11]
       jmp       short M47_L05
M47_L07:
       mov       rcx,rbp
       mov       rdx,rdi
       mov       rax,[rbp]
       mov       rax,[rax+48]
       call      qword ptr [rax+10]
       jmp       short M47_L05
M47_L08:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
; Total bytes of code 249
```
```assembly
; System.Linq.Enumerable.ICollectionToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.ICollection`1<System.__Canon>)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],30
       jle       short M48_L02
       mov       r11,[rcx+30]
       test      r11,r11
       je        short M48_L02
M48_L00:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       edi,eax
       test      edi,edi
       jne       short M48_L04
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],38
       jle       short M48_L03
       mov       rcx,[rcx+38]
       test      rcx,rcx
       je        short M48_L03
M48_L01:
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8AF9674C8]; System.Array.Empty[[System.__Canon, System.Private.CoreLib]]()
M48_L02:
       mov       rcx,rbx
       mov       rdx,7FF8B05903C0
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M48_L00
M48_L03:
       mov       rcx,rbx
       mov       rdx,7FF8B05903D8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M48_L01
M48_L04:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],40
       jle       short M48_L07
       mov       rcx,[rcx+40]
       test      rcx,rcx
       je        short M48_L07
M48_L05:
       movsxd    rdx,edi
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rdi,rax
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+8],48
       jle       short M48_L08
       mov       r11,[rcx+48]
       test      r11,r11
       je        short M48_L08
M48_L06:
       mov       rcx,rsi
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [r11]
       mov       rax,rdi
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M48_L07:
       mov       rcx,rbx
       mov       rdx,7FF8B05903F8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M48_L05
M48_L08:
       mov       rcx,rbx
       mov       rdx,7FF8B0590400
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M48_L06
; Total bytes of code 249
```
```assembly
; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].Fill(System.__Canon[], System.Span`1<System.__Canon>)
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,30
       mov       rbx,rdx
       mov       rsi,[r8]
       mov       edi,[r8+8]
       lea       r11,[System.Linq.Utilities.CombineSelectors[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Func`2<System.__Canon,System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)]
       xor       edx,edx
       cmp       [rcx],ecx
       call      qword ptr [r11]
       mov       rcx,rax
       mov       ebp,[rbx+8]
       mov       r8d,ebp
       mov       rdx,rbx
       cmp       [rcx],ecx
       call      qword ptr [7FF91AA40498]; Precode of System.Linq.Enumerable+EnumerableSorter`1[[System.__Canon, System.Private.CoreLib]].Sort(System.__Canon[], Int32)
       mov       r14,rax
       xor       r15d,r15d
       test      edi,edi
       jle       short M49_L01
       test      r14,r14
       je        short M49_L02
       cmp       [r14+8],edi
       jl        short M49_L02
M49_L00:
       mov       ecx,r15d
       mov       [rsp+28],rcx
       lea       rcx,[rsi+rcx*8]
       mov       rdx,[rsp+28]
       mov       r13d,[r14+rdx*4+10]
       cmp       r13d,ebp
       jae       short M49_L03
       mov       edx,r13d
       mov       rdx,[rbx+rdx*8+10]
       call      qword ptr [7FF91AA3C648]; CORINFO_HELP_CHECKED_ASSIGN_REF
       inc       r15d
       cmp       r15d,edi
       jl        short M49_L00
M49_L01:
       add       rsp,30
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       ret
M49_L02:
       mov       ecx,r15d
       lea       rcx,[rsi+rcx*8]
       cmp       r15d,[r14+8]
       jae       short M49_L03
       mov       edx,r15d
       mov       r13d,[r14+rdx*4+10]
       cmp       r13d,ebp
       jae       short M49_L03
       mov       edx,r13d
       mov       rdx,[rbx+rdx*8+10]
       call      qword ptr [7FF91AA3C648]; CORINFO_HELP_CHECKED_ASSIGN_REF
       inc       r15d
       cmp       r15d,edi
       jl        short M49_L02
       jmp       short M49_L01
M49_L03:
       call      qword ptr [7FF91AA3C638]
       int       3
; Total bytes of code 200
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstance_Helper(Void*, System.Object)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rax,26D3D800038
       mov       r8,[rax]
       mov       r10,[rdx]
       add       r8,10
       rorx      rax,r10,20
       xor       rax,rcx
       mov       r9,9E3779B97F4A7C15
       imul      rax,r9
       mov       r9d,[r8]
       shrx      r9,rax,r9
       xor       r11d,r11d
M50_L00:
       lea       eax,[r9+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[r8+rax*8]
       mov       ebx,[rax]
       mov       rsi,[rax+8]
       and       ebx,0FFFFFFFE
       cmp       rsi,r10
       jne       short M50_L02
       mov       rsi,rcx
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M50_L02
       cmp       ebx,[rax]
       jne       short M50_L03
M50_L01:
       cmp       esi,1
       jne       short M50_L04
       mov       rax,rdx
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M50_L02:
       test      ebx,ebx
       je        short M50_L03
       inc       r11d
       add       r9d,r11d
       and       r9d,[r8+4]
       cmp       r11d,8
       jl        short M50_L00
M50_L03:
       mov       esi,2
       jmp       short M50_L01
M50_L04:
       test      esi,esi
       jne       short M50_L05
       xor       eax,eax
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M50_L05:
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny_NoCacheLookup(Void*, System.Object)
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 173
```
```assembly
; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].AddNonICollectionRangeInlined(System.Collections.Generic.IEnumerable`1<System.__Canon>)
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,48
       lea       rbp,[rsp+70]
       mov       [rbp-50],rsp
       mov       [rbp-30],rdx
       mov       [rbp+10],rcx
       mov       rbx,rdx
       mov       rsi,r8
       mov       rdi,[rcx+0F8]
       mov       r14d,[rcx+100]
       mov       edx,[rcx+8]
       mov       [rbp-34],edx
       mov       rdx,[rbx+30]
       mov       rdx,[rdx]
       cmp       qword ptr [rdx+8],120
       jle       short M51_L01
       mov       r11,[rdx+120]
       test      r11,r11
       je        short M51_L01
M51_L00:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       rsi,rax
       mov       [rbp-40],rsi
       jmp       short M51_L02
M51_L01:
       mov       rcx,rbx
       mov       rdx,7FF8B06AAB58
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M51_L00
M51_L02:
       mov       rcx,offset MT_System.Linq.Enumerable+SelectManySingleSelectorIterator<Hl7.Fhir.Model.PocoNode, Hl7.Fhir.Model.PocoNode>
       cmp       [rsi],rcx
       jne       short M51_L06
       mov       rcx,rsi
       call      qword ptr [7FF8B018FC50]; System.Linq.Enumerable+SelectManySingleSelectorIterator`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].MoveNext()
M51_L03:
       test      eax,eax
       je        near ptr M51_L09
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],128
       jle       short M51_L05
       mov       r11,[rcx+128]
       test      r11,r11
       je        short M51_L05
M51_L04:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       r15,rax
       cmp       [rbp-34],r14d
       jae       short M51_L07
       mov       ecx,[rbp-34]
       lea       rcx,[rdi+rcx*8]
       mov       rdx,r15
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       ecx,[rbp-34]
       inc       ecx
       mov       [rbp-34],ecx
       jmp       short M51_L02
M51_L05:
       mov       rcx,rbx
       mov       rdx,7FF8B06AAB70
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M51_L04
M51_L06:
       mov       rcx,rsi
       mov       r11,7FF8AF573280
       call      qword ptr [r11]
       jmp       short M51_L03
M51_L07:
       mov       rcx,[rbp+10]
       mov       rdx,rbx
       mov       r8d,10
       call      qword ptr [7FF8AFEA69D0]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].Expand(Int32)
       mov       rcx,[rbp+10]
       mov       rdi,[rcx+0F8]
       mov       r14d,[rcx+100]
       test      r14d,r14d
       je        short M51_L08
       mov       rcx,rdi
       mov       rdx,r15
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       dword ptr [rbp-34],1
       jmp       near ptr M51_L02
M51_L08:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
M51_L09:
       mov       rcx,rsp
       call      M51_L10
       nop
       mov       ecx,[rbp-34]
       mov       rax,[rbp+10]
       mov       [rax+8],ecx
       add       rsp,48
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
M51_L10:
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+70]
       mov       rsi,[rbp-40]
       test      rsi,rsi
       je        short M51_L15
       mov       rcx,offset MT_System.Linq.Enumerable+SelectManySingleSelectorIterator<Hl7.Fhir.Model.PocoNode, Hl7.Fhir.Model.PocoNode>
       cmp       [rsi],rcx
       jne       short M51_L14
       cmp       qword ptr [rsi+30],0
       je        short M51_L11
       mov       rcx,[rsi+30]
       mov       r11,7FF8AF573298
       call      qword ptr [r11]
       xor       ecx,ecx
       mov       [rsi+30],rcx
M51_L11:
       mov       rcx,[rsi+28]
       test      rcx,rcx
       je        short M51_L13
       mov       r11,offset MT_<>z__ReadOnlySingleElementList<Hl7.Fhir.Model.PocoNode>+Enumerator
       cmp       [rcx],r11
       je        short M51_L12
       mov       r11,7FF8AF573290
       call      qword ptr [r11]
M51_L12:
       xor       ecx,ecx
       mov       [rsi+28],rcx
M51_L13:
       xor       ecx,ecx
       mov       [rsi+8],rcx
       mov       dword ptr [rsi+14],0FFFFFFFF
       jmp       short M51_L15
M51_L14:
       mov       rcx,rsi
       mov       r11,7FF8AF573288
       call      qword ptr [r11]
M51_L15:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 534
```
```assembly
; System.Array.Empty[[System.__Canon, System.Private.CoreLib]]()
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rdx,[rcx+18]
       mov       rdx,[rdx+18]
       test      rdx,rdx
       je        short M52_L01
M52_L00:
       mov       rcx,rdx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,[rax]
       add       rsp,28
       ret
M52_L01:
       mov       rdx,7FF8B043F788
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rdx,rax
       jmp       short M52_L00
; Total bytes of code 58
```
```assembly
; System.Buffer.BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rsi,rcx
       mov       rdi,rdx
       mov       rbx,r8
       cmp       rbx,4000
       ja        short M53_L00
       mov       rcx,7FF8B06F62C8
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rcx,rsi
       mov       rdx,rdi
       mov       r8,rbx
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       near ptr System.Buffer.__BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
M53_L00:
       mov       rcx,7FF8B06F62CC
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rcx,rsi
       mov       rdx,rdi
       mov       r8,rbx
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8B0506478]
; Total bytes of code 98
```
```assembly
; <PrivateImplementationDetails>.InlineArrayAsReadOnlySpan[[System.Collections.Generic.SegmentedArrayBuilder`1+Arrays[[System.__Canon, System.Private.CoreLib]], System.Linq],[System.__Canon, System.Private.CoreLib]](Arrays<System.__Canon> ByRef, Int32)
       push      rbp
       sub       rsp,40
       lea       rbp,[rsp+40]
       mov       [rbp-8],rdx
       mov       [rbp+10],rcx
       mov       [rbp+18],rdx
       mov       [rbp+20],r8
       mov       [rbp+28],r9d
       mov       rax,[rbp+18]
       mov       rax,[rax+18]
       mov       rax,[rax+18]
       mov       [rbp-18],rax
       cmp       qword ptr [rbp-18],0
       je        short M54_L00
       mov       rax,[rbp-18]
       mov       [rbp-10],rax
       jmp       short M54_L01
M54_L00:
       mov       rcx,[rbp+18]
       mov       rdx,7FF8AFEECAC0
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       [rbp-10],rax
M54_L01:
       mov       rcx,[rbp+10]
       mov       rdx,[rbp-10]
       mov       r8,[rbp+20]
       mov       r9d,[rbp+28]
       call      qword ptr [7FF8AFEA6C88]; System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Int32)
       mov       rax,[rbp+10]
       add       rsp,40
       pop       rbp
       ret
; Total bytes of code 118
```
```assembly
; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].ReturnArrays(Int32)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       mov       [rsp+40],rdx
       mov       rsi,rcx
       mov       rdi,rdx
       mov       ebx,r8d
       cmp       [rsi],sil
       lea       rbp,[rsi+10]
       dec       ebx
       cmp       ebx,1B
       ja        near ptr M55_L31
       test      ebx,ebx
       jle       short M55_L02
       xor       r14d,r14d
       mov       r15d,ebx
M55_L00:
       mov       r13,[r14+rbp]
       mov       rcx,r13
       call      qword ptr [7FF8AFEA6D60]; System.Array.Clear(System.Array)
       mov       rcx,[rdi+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],108
       jle       near ptr M55_L08
       mov       rcx,[rcx+108]
       test      rcx,rcx
       je        near ptr M55_L08
M55_L01:
       call      qword ptr [7FF8AFEA6AA8]; System.Buffers.ArrayPool`1[[System.__Canon, System.Private.CoreLib]].get_Shared()
       mov       rcx,rax
       mov       rdx,r13
       xor       r8d,r8d
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+28]
       add       r14,8
       dec       r15d
       jne       short M55_L00
M55_L02:
       cmp       ebx,1B
       jae       near ptr M55_L32
       mov       r8d,ebx
       mov       r12,[rbp+r8*8]
       mov       r8d,[rsi+8]
       mov       rcx,r12
       xor       edx,edx
       call      qword ptr [7FF8AFEA6DF0]; System.Array.Clear(System.Array, Int32, Int32)
       mov       rcx,[rdi+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],108
       jle       near ptr M55_L09
       mov       rcx,[rcx+108]
       test      rcx,rcx
       je        near ptr M55_L09
M55_L03:
       call      qword ptr [7FF8AFEA6AA8]; System.Buffers.ArrayPool`1[[System.__Canon, System.Private.CoreLib]].get_Shared()
       mov       rbx,rax
       mov       rcx,offset MT_System.Buffers.SharedArrayPool<Hl7.Fhir.Introspection.PropertyMapping>
       cmp       [rbx],rcx
       jne       near ptr M55_L30
       test      r12,r12
       je        near ptr M55_L29
       mov       ecx,[r12+8]
       dec       ecx
       or        ecx,0F
       xor       esi,esi
       lzcnt     esi,ecx
       xor       esi,1F
       add       esi,0FFFFFFFD
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+208],0B
       jle       near ptr M55_L10
       mov       rcx,[rcx+210]
       mov       rax,[rcx+58]
       test      rax,rax
       je        near ptr M55_L10
M55_L04:
       mov       r8,[rax+10]
       test      r8,r8
       je        near ptr M55_L11
M55_L05:
       xor       ebp,ebp
       mov       r14d,1
       mov       ecx,[r8+8]
       cmp       ecx,esi
       jbe       short M55_L06
       mov       ebp,1
       mov       ecx,10
       shlx      ecx,ecx,esi
       cmp       [r12+8],ecx
       jne       near ptr M55_L25
       mov       ecx,esi
       shl       rcx,4
       lea       rdi,[r8+rcx+10]
       mov       r15,[rdi]
       mov       rcx,rdi
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [rdi+8],ecx
       test      r15,r15
       jne       near ptr M55_L12
M55_L06:
       mov       rcx,26D3D8002B8
       mov       rdi,[rcx]
       cmp       byte ptr [rdi+9D],0
       jne       near ptr M55_L26
M55_L07:
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M55_L08:
       mov       rcx,rdi
       mov       rdx,7FF8B065D9F0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M55_L01
M55_L09:
       mov       rcx,rdi
       mov       rdx,7FF8B065D9F0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M55_L03
M55_L10:
       mov       ecx,0B
       call      CORINFO_HELP_GETDYNAMIC_GCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       jmp       near ptr M55_L04
M55_L11:
       mov       rcx,rbx
       call      qword ptr [7FF8AFEA6DA8]; System.Buffers.SharedArrayPool`1[[System.__Canon, System.Private.CoreLib]].InitializeTlsBucketsAndTrimming()
       mov       r8,rax
       jmp       near ptr M55_L05
M55_L12:
       mov       rcx,[rbx+10]
       cmp       esi,[rcx+8]
       jae       near ptr M55_L32
       mov       edx,esi
       mov       rax,[rcx+rdx*8+10]
       test      rax,rax
       jne       short M55_L13
       mov       rcx,rbx
       mov       edx,esi
       call      qword ptr [7FF8B050C5A0]
M55_L13:
       mov       rdi,[rax+8]
       mov       rcx,offset MT_System.Threading.ProcessorIdCache
       call      CORINFO_HELP_GET_NONGCSTATIC_BASE
       cmp       byte ptr [7FF8AF56B144],0
       jne       short M55_L15
       mov       ecx,0C
       call      CORINFO_HELP_GETDYNAMIC_NONGCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       mov       r14d,[rax+10]
       mov       ecx,0C
       call      CORINFO_HELP_GETDYNAMIC_NONGCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       lea       ecx,[r14-1]
       mov       [rax+10],ecx
       movzx     eax,r14w
       test      eax,eax
       je        short M55_L14
       sar       r14d,10
       jmp       short M55_L16
M55_L14:
       call      qword ptr [7FF8B0505050]
       mov       r14d,eax
       jmp       short M55_L16
M55_L15:
       call      qword ptr [7FF8B0505038]
       mov       r14d,eax
M55_L16:
       mov       rcx,offset MT_System.Buffers.SharedArrayPoolStatics
       call      CORINFO_HELP_GET_NONGCSTATIC_BASE
       mov       eax,r14d
       xor       edx,edx
       div       dword ptr [7FF8AF56B138]
       mov       r14d,edx
       xor       r13d,r13d
M55_L17:
       cmp       [rdi+8],r13d
       jle       near ptr M55_L23
       cmp       r14d,[rdi+8]
       jae       near ptr M55_L32
       mov       ecx,r14d
       mov       rax,[rdi+rcx*8+10]
       mov       [rsp+30],rax
       cmp       [rax],al
       xor       edx,edx
       mov       [rsp+3C],edx
       mov       rcx,rax
       call      System.Threading.Monitor.Enter(System.Object)
       mov       rax,[rsp+30]
       mov       rcx,[rax+8]
       mov       r8d,[rax+10]
       mov       [rsp+38],r8d
       cmp       [rcx+8],r8d
       jbe       short M55_L19
       test      r8d,r8d
       jne       short M55_L20
       xor       edx,edx
       mov       [rax+14],edx
M55_L18:
       movsxd    rdx,r8d
       lea       rcx,[rcx+rdx*8+10]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       ecx,[rsp+38]
       inc       ecx
       mov       rax,[rsp+30]
       mov       [rax+10],ecx
       mov       dword ptr [rsp+3C],1
M55_L19:
       mov       rcx,rax
       call      System.Threading.Monitor.Exit(System.Object)
       cmp       dword ptr [rsp+3C],0
       je        short M55_L21
       mov       ecx,1
       jmp       short M55_L24
M55_L20:
       jmp       short M55_L18
M55_L21:
       inc       r14d
       cmp       [rdi+8],r14d
       jne       short M55_L22
       xor       r14d,r14d
M55_L22:
       inc       r13d
       jmp       near ptr M55_L17
M55_L23:
       xor       ecx,ecx
M55_L24:
       mov       r14d,ecx
       jmp       near ptr M55_L06
M55_L25:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       call      qword ptr [7FF8B0505C50]
       mov       rbx,rax
       mov       ecx,25F
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rdx,rbx
       mov       rcx,r12
       call      qword ptr [7FF8AF966F28]
       mov       rcx,r12
       call      CORINFO_HELP_THROW
       int       3
M55_L26:
       cmp       dword ptr [r12+8],0
       je        near ptr M55_L07
       mov       rcx,r12
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r15d,eax
       mov       r13d,[r12+8]
       mov       rcx,rbx
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       [rsp+20],eax
       mov       rcx,rdi
       mov       r8d,r15d
       mov       r9d,r13d
       mov       edx,3
       call      qword ptr [7FF8B0505C68]
       test      r14d,ebp
       jne       near ptr M55_L07
       mov       rcx,r12
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r14d,eax
       mov       r12d,[r12+8]
       mov       rcx,rbx
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r9d,eax
       test      ebp,ebp
       jne       short M55_L27
       mov       ecx,0FFFFFFFF
       mov       edx,1
       jmp       short M55_L28
M55_L27:
       mov       ecx,esi
       xor       edx,edx
M55_L28:
       mov       [rsp+20],ecx
       mov       [rsp+28],edx
       mov       rcx,rdi
       mov       edx,r14d
       mov       r8d,r12d
       call      qword ptr [7FF8B0505C80]
       jmp       near ptr M55_L07
M55_L29:
       mov       ecx,2
       call      qword ptr [7FF8AF61FB28]
       int       3
M55_L30:
       mov       rcx,rbx
       mov       rdx,r12
       xor       r8d,r8d
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+28]
       jmp       near ptr M55_L07
M55_L31:
       call      qword ptr [7FF8AF827798]
       int       3
M55_L32:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 1137
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GrowTable(Tables<System.__Canon,System.__Canon>, Boolean, Boolean)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,88
       lea       rbp,[rsp+0C0]
       mov       [rbp-0A0],rsp
       mov       [rbp-40],rcx
       mov       [rbp+10],rcx
       mov       rbx,rdx
       mov       esi,r8d
       mov       edi,r9d
       xor       eax,eax
       mov       [rbp-48],eax
       mov       rax,[rcx+8]
       mov       rax,[rax+18]
       cmp       dword ptr [rax+8],0
       jbe       near ptr M56_L15
       mov       rcx,[rax+10]
       call      qword ptr [7FF924A7FFC0]; System.Threading.Monitor.Enter(System.Object)
       mov       dword ptr [rbp-48],1
       mov       rcx,[rbp+10]
       cmp       rbx,[rcx+8]
       jne       near ptr M56_L17
       mov       rax,[rbx+10]
       mov       r14d,[rax+8]
       xor       r15d,r15d
       test      dil,dil
       jne       near ptr M56_L12
M56_L00:
       test      sil,sil
       je        short M56_L02
       test      r15,r15
       jne       short M56_L01
       call      qword ptr [7FF924A807F8]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetCountNoLocks()
       mov       rcx,[rbx+10]
       mov       ecx,[rcx+8]
       shr       ecx,2
       cmp       eax,ecx
       mov       rcx,[rbp+10]
       jl        near ptr M56_L13
M56_L01:
       mov       rax,[rbx+10]
       mov       eax,[rax+8]
       add       eax,eax
       js        near ptr M56_L14
       mov       ecx,eax
       call      qword ptr [7FF924A80330]; Precode of System.Collections.HashHelpers.GetPrime(Int32)
       mov       r14d,eax
       call      qword ptr [7FF924A7FEB8]; Precode of System.Array.get_MaxLength()
       cmp       eax,r14d
       jl        near ptr M56_L14
M56_L02:
       mov       rcx,[rbp+10]
       mov       rsi,[rbx+18]
       mov       rdi,rsi
       cmp       byte ptr [rcx+14],0
       je        short M56_L04
       cmp       dword ptr [rsi+8],400
       jge       short M56_L04
       mov       eax,[rsi+8]
       add       eax,eax
       movsxd    rcx,eax
       call      qword ptr [7FF924A7FE60]
       mov       rdi,rax
       mov       r8d,[rsi+8]
       mov       rcx,rsi
       mov       rdx,rdi
       call      qword ptr [7FF924A7FEA8]; Precode of System.Array.Copy(System.Array, System.Array, Int32)
       mov       rax,[rbx+18]
       mov       esi,[rax+8]
       mov       r13d,[rdi+8]
       cmp       r13d,esi
       jle       short M56_L04
M56_L03:
       call      qword ptr [7FF924A7FDB8]
       mov       r8,rax
       movsxd    rdx,esi
       mov       rcx,rdi
       call      qword ptr [7FF924A7F2A0]; Precode of System.Runtime.CompilerServices.CastHelpers.StelemRef(System.Object[], IntPtr, System.Object)
       inc       esi
       cmp       r13d,esi
       jg        short M56_L03
M56_L04:
       mov       rcx,[rbp+10]
       mov       rsi,[rcx]
       mov       rcx,rsi
       call      qword ptr [7FF924A7F980]
       mov       rcx,rax
       movsxd    rdx,r14d
       call      qword ptr [7FF924A7F2B8]; CORINFO_HELP_NEWARR_1_DIRECT
       mov       r14,rax
       mov       [rbp-58],r14
       mov       ecx,[rdi+8]
       call      qword ptr [7FF924A7FE68]
       mov       r13,rax
       mov       r12,r15
       test      r12,r12
       jne       short M56_L05
       mov       r12,[rbx+8]
M56_L05:
       mov       rcx,rsi
       call      qword ptr [7FF924A7F6F0]
       mov       rcx,rax
       call      qword ptr [7FF924A7F2B0]; CORINFO_HELP_NEWFAST
       mov       [rbp-70],rax
       lea       rcx,[rax+10]
       mov       rdx,r14
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-70]
       lea       rcx,[rax+18]
       mov       rdx,rdi
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-70]
       lea       rcx,[rax+20]
       mov       rdx,r13
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-70]
       lea       rcx,[rax+8]
       mov       rdx,r12
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,0FFFFFFFFFFFFFFFF
       mov       ecx,[r14+8]
       xor       edx,edx
       div       rcx
       inc       rax
       mov       r12,[rbp-70]
       mov       [r12+28],rax
       mov       rcx,rsi
       call      qword ptr [7FF924A7F6B8]
       mov       rcx,rax
       lea       r8,[rbp-48]
       mov       rdx,rbx
       call      qword ptr [7FF924A80818]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].AcquirePostFirstLock(Tables<System.__Canon,System.__Canon>, Int32 ByRef)
       mov       rbx,[rbx+10]
       mov       eax,[rbx+8]
       test      eax,eax
       jle       short M56_L08
       add       rbx,10
M56_L06:
       mov       r8,[rbx]
       test      r8,r8
       jne       short M56_L09
M56_L07:
       add       rbx,8
       dec       eax
       jne       short M56_L06
M56_L08:
       mov       eax,[r14+8]
       xor       edx,edx
       div       dword ptr [rdi+8]
       mov       ecx,1
       cmp       eax,1
       cmovg     ecx,eax
       mov       rax,[rbp+10]
       mov       [rax+10],ecx
       lea       rcx,[rax+8]
       mov       rdx,r12
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbp+10]
       jmp       near ptr M56_L17
M56_L09:
       test      r15,r15
       jne       near ptr M56_L11
       mov       [rbp-60],r8
       mov       edx,[r8+20]
       mov       [rbp-50],eax
M56_L10:
       mov       r8,[rbp-60]
       mov       r10,[r8+18]
       mov       [rbp-78],r10
       mov       rcx,[r12+10]
       mov       [rbp-4C],edx
       mov       r9d,edx
       imul      r9,[r12+28]
       shr       r9,20
       inc       r9
       mov       r11d,[rcx+8]
       mov       r14d,r11d
       imul      r9,r14
       shr       r9,20
       mov       r14,[r12+18]
       mov       eax,r9d
       xor       edx,edx
       div       dword ptr [r14+8]
       mov       r14d,edx
       cmp       r9d,r11d
       jae       near ptr M56_L15
       mov       eax,r9d
       lea       rax,[rcx+rax*8+10]
       mov       [rbp-68],rax
       mov       rcx,rsi
       call      qword ptr [7FF924A7F6D8]
       mov       rcx,rax
       call      qword ptr [7FF924A7F2B0]; CORINFO_HELP_NEWFAST
       mov       [rbp-80],rax
       mov       r8,[rbp-60]
       mov       rdx,[r8+8]
       mov       r8,[r8+10]
       mov       [rbp-88],r8
       mov       r10,[rbp-68]
       mov       r9,[r10]
       mov       [rbp-90],r9
       lea       rcx,[rax+8]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-80]
       lea       rcx,[rax+10]
       mov       rdx,[rbp-88]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-80]
       lea       rcx,[rax+18]
       mov       rdx,[rbp-90]
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-80]
       mov       edx,[rbp-4C]
       mov       [rax+20],edx
       mov       rcx,[rbp-68]
       mov       rdx,rax
       call      qword ptr [7FF924A7F288]; CORINFO_HELP_ASSIGN_REF
       cmp       r14d,[r13+8]
       jae       near ptr M56_L15
       mov       ecx,r14d
       lea       rcx,[r13+rcx*4+10]
       mov       edx,[rcx]
       add       edx,1
       jo        near ptr M56_L16
       mov       [rcx],edx
       mov       r14,[rbp-78]
       test      r14,r14
       mov       r8,r14
       mov       eax,[rbp-50]
       mov       r14,[rbp-58]
       jne       near ptr M56_L09
       jmp       near ptr M56_L07
M56_L11:
       mov       [rbp-60],r8
       mov       [rbp-50],eax
       mov       rcx,[rbp+10]
       mov       rcx,[rcx]
       call      qword ptr [7FF924A7FB38]
       mov       r8,[rbp-60]
       mov       rdx,[r8+8]
       mov       rcx,r15
       mov       r11,rax
       call      qword ptr [rax]
       mov       r8d,eax
       mov       edx,r8d
       jmp       near ptr M56_L10
M56_L12:
       mov       rcx,[rbx+8]
       call      qword ptr [7FF924A7FE80]
       mov       rdi,rax
       test      rdi,rdi
       mov       rcx,[rbp+10]
       je        near ptr M56_L00
       mov       rax,[rcx]
       mov       r15,rax
       mov       rcx,r15
       call      qword ptr [7FF924A7F528]
       mov       r15,rax
       mov       rcx,rdi
       lea       r11,[System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)]
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,r15
       call      qword ptr [7FF924A7F2C0]; Precode of System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r15,rax
       mov       rcx,[rbp+10]
       jmp       near ptr M56_L00
M56_L13:
       shl       dword ptr [rcx+10],1
       cmp       dword ptr [rcx+10],0
       jge       short M56_L17
       mov       dword ptr [rcx+10],7FFFFFFF
       mov       rcx,[rbp+10]
       jmp       short M56_L17
M56_L14:
       call      qword ptr [7FF924A7FEB8]; Precode of System.Array.get_MaxLength()
       mov       r14d,eax
       mov       rcx,[rbp+10]
       mov       dword ptr [rcx+10],7FFFFFFF
       jmp       near ptr M56_L02
M56_L15:
       call      qword ptr [7FF924A7F280]
       int       3
M56_L16:
       call      qword ptr [7FF924A7F278]
       int       3
M56_L17:
       mov       edx,[rbp-48]
       call      qword ptr [7FF924A80828]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].ReleaseLocks(Int32)
       nop
       add       rsp,88
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+0C0]
       mov       rcx,[rbp+10]
       mov       edx,[rbp-48]
       call      qword ptr [7FF924A80828]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].ReleaseLocks(Int32)
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 1168
```
```assembly
; Hl7.Fhir.Utility.AnnotationList+<>c.<.ctor>b__13_0()
       push      rbx
       sub       rsp,30
       mov       rcx,offset MT_System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       xor       ecx,ecx
       mov       [rsp+20],rcx
       mov       rcx,rbx
       mov       edx,1C
       mov       r8d,1F
       mov       r9d,1
       call      qword ptr [7FF8AF967528]; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, Int32, Boolean, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       rax,rbx
       add       rsp,30
       pop       rbx
       ret
; Total bytes of code 65
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, Int32, Boolean, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       mov       [rsp+30],rcx
       mov       rsi,rcx
       mov       edi,edx
       mov       ebx,r8d
       mov       ebp,r9d
       mov       r14,[rsp+0A0]
       test      edi,edi
       jle       near ptr M58_L14
M58_L00:
       test      ebx,ebx
       jl        near ptr M58_L18
       cmp       ebx,edi
       cmovl     ebx,edi
       mov       ecx,ebx
       call      qword ptr [7FF8AF967558]; System.Collections.HashHelpers.GetPrime(Int32)
       mov       ebx,eax
       movsxd    rdx,edi
       mov       rcx,offset MT_System.Object[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       r15,rax
       mov       r13d,[r15+8]
       test      r13d,r13d
       je        near ptr M58_L20
       lea       rcx,[r15+10]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       mov       edi,1
       cmp       r13d,1
       jle       short M58_L02
       mov       r12,offset MT_System.Object
M58_L01:
       mov       rcx,r12
       call      CORINFO_HELP_NEWSFAST
       lea       rcx,[r15+rdi*8+10]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       inc       edi
       cmp       r13d,edi
       jg        short M58_L01
M58_L02:
       mov       edx,r13d
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       mov       rdi,rax
       mov       r12,[rsi]
       mov       rcx,r12
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+0C8]
       test      rax,rax
       je        near ptr M58_L10
       mov       rcx,rax
M58_L03:
       movsxd    rdx,ebx
       call      CORINFO_HELP_NEWARR_1_VC
       mov       rbx,rax
       test      r14,r14
       jne       short M58_L05
       mov       rcx,[r12+30]
       mov       rcx,[rcx]
       mov       rcx,[rcx+0D0]
       test      rcx,rcx
       je        near ptr M58_L11
M58_L04:
       call      qword ptr [7FF8AF615AD0]; System.Collections.Generic.EqualityComparer`1[[System.__Canon, System.Private.CoreLib]].get_Default()
       mov       r14,rax
M58_L05:
       mov       rcx,[r12+30]
       mov       rcx,[rcx]
       mov       rax,offset MT_System.String
       cmp       [rcx],rax
       je        near ptr M58_L15
M58_L06:
       mov       rcx,[r12+30]
       mov       rcx,[rcx]
       mov       rcx,[rcx+0D0]
       test      rcx,rcx
       je        near ptr M58_L12
M58_L07:
       call      qword ptr [7FF8AF615AD0]; System.Collections.Generic.EqualityComparer`1[[System.__Canon, System.Private.CoreLib]].get_Default()
       cmp       rax,r14
       jne       short M58_L08
       mov       byte ptr [rsi+15],1
M58_L08:
       mov       rcx,[r12+30]
       mov       rcx,[rcx]
       mov       rcx,[rcx+0D8]
       test      rcx,rcx
       je        near ptr M58_L13
M58_L09:
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       lea       rcx,[r12+10]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+18]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+20]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+8]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,0FFFFFFFFFFFFFFFF
       mov       ebx,[rbx+8]
       mov       ecx,ebx
       xor       edx,edx
       div       rcx
       inc       rax
       mov       [r12+28],rax
       lea       rcx,[rsi+8]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rsi+14],bpl
       mov       eax,ebx
       xor       edx,edx
       div       r13d
       mov       [rsi+10],eax
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M58_L10:
       mov       rdx,7FF8B05925C8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M58_L03
M58_L11:
       mov       rcx,r12
       mov       rdx,7FF8B05925E0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M58_L04
M58_L12:
       mov       rcx,r12
       mov       rdx,7FF8B05925E0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M58_L07
M58_L13:
       mov       rcx,r12
       mov       rdx,7FF8B05925F0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M58_L09
M58_L14:
       cmp       edi,0FFFFFFFF
       jne       near ptr M58_L19
       cmp       [rsi],esi
       mov       edi,1C
       jmp       near ptr M58_L00
M58_L15:
       mov       rcx,r14
       call      qword ptr [7FF8AF6161C0]; System.Collections.Generic.NonRandomizedStringEqualityComparer.GetStringComparer(System.Object)
       test      rax,rax
       mov       [rsp+28],rax
       je        near ptr M58_L06
       mov       rcx,[r12+30]
       mov       rcx,[rcx]
       mov       rcx,[rcx+0E0]
       test      rcx,rcx
       je        short M58_L16
       mov       rax,[rsp+28]
       jmp       short M58_L17
M58_L16:
       mov       rcx,r12
       mov       rdx,7FF8B0592608
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       mov       rax,[rsp+28]
M58_L17:
       mov       rdx,rax
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r14,rax
       jmp       near ptr M58_L08
M58_L18:
       mov       ecx,0BE4
       mov       rdx,7FF8AF997C50
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       ecx,ebx
       call      qword ptr [7FF8B0504CC0]
       int       3
M58_L19:
       mov       rcx,offset MT_System.ArgumentOutOfRangeException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,0BC2
       mov       rdx,7FF8AF997C50
       call      CORINFO_HELP_STRCNS
       mov       rdi,rax
       call      qword ptr [7FF8AFB16FB8]
       mov       r8,rax
       mov       rdx,rdi
       mov       rcx,rbx
       call      qword ptr [7FF8AFB16FD0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M58_L20:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 812
```
```assembly
; <>z__ReadOnlySingleElementList`1+Enumerator[[System.__Canon, System.Private.CoreLib]].System.Collections.IEnumerator.MoveNext()
       cmp       byte ptr [rcx+10],0
       jne       short M59_L00
       mov       byte ptr [rcx+10],1
       mov       eax,1
       ret
M59_L00:
       xor       eax,eax
       ret
; Total bytes of code 19
```
```assembly
; Hl7.Fhir.Introspection.ModelInspector+<>c__DisplayClass2_0.<ForAssembly>g__configureInspector|1()
       push      rbp
       sub       rsp,160
       lea       rbp,[rsp+160]
       xor       eax,eax
       mov       [rbp-138],rax
       vxorps    xmm4,xmm4,xmm4
       mov       rax,0FFFFFFFFFFFFFF10
M60_L00:
       vmovdqa   xmmword ptr [rbp+rax-40],xmm4
       vmovdqa   xmmword ptr [rbp+rax-30],xmm4
       vmovdqa   xmmword ptr [rbp+rax-20],xmm4
       add       rax,30
       jne       short M60_L00
       mov       [rbp-40],rax
       mov       [rbp-140],rsp
       mov       [rbp+10],rcx
       mov       rax,[rbp+10]
       mov       rdx,[rax+8]
       mov       rcx,7FF8AFDE5A28
       call      qword ptr [7FF8AF82DC98]; System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)
       mov       [rbp-50],rax
       mov       dword ptr [rbp-0D0],3E8
       cmp       qword ptr [rbp-50],0
       jne       near ptr M60_L01
       lea       rcx,[rbp-78]
       mov       edx,58
       mov       r8d,2
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,137E0
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-0D8],rax
       mov       rdx,[rbp-0D8]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF61C000]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(System.String)
       mov       rax,[rbp+10]
       mov       rax,[rax+8]
       mov       [rbp-0A8],rax
       mov       rcx,[rbp-0A8]
       mov       rdx,7FF8AFDE6078
       call      CORINFO_HELP_CLASSPROFILE32
       mov       rax,[rbp-0A8]
       mov       [rbp-0E0],rax
       mov       rcx,[rbp-0E0]
       mov       rax,[rbp-0E0]
       mov       rax,[rax]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       mov       [rbp-0E8],rax
       mov       rdx,[rbp-0E8]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,137F4
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-0F0],rax
       mov       rdx,[rbp-0F0]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF61C000]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(System.String)
       mov       ecx,13846
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-0F8],rax
       mov       rdx,[rbp-0F8]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF61C000]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(System.String)
       mov       ecx,13880
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-100],rax
       mov       rdx,[rbp-100]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,138B6
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-108],rax
       mov       rdx,[rbp-108]
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF61C000]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(System.String)
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0A0],rax
       lea       rcx,[rbp-78]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       [rbp-110],rax
       mov       rdx,[rbp-110]
       mov       rcx,[rbp-0A0]
       call      qword ptr [7FF8AF966E68]
       mov       rcx,[rbp-0A0]
       call      CORINFO_HELP_THROW
       int       3
M60_L01:
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ModelInspector
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-90],rax
       mov       rcx,[rbp-50]
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFC8ECE8]; Hl7.Fhir.Introspection.FhirModelAttribute.get_Since()
       mov       [rbp-114],eax
       mov       edx,[rbp-114]
       mov       rcx,[rbp-90]
       call      qword ptr [7FF8AFC8ED00]; Hl7.Fhir.Introspection.ModelInspector..ctor(Hl7.Fhir.Specification.FhirRelease)
       mov       rax,[rbp-90]
       mov       [rbp-40],rax
       mov       rcx,offset MT_System.Collections.Generic.List<System.Reflection.Assembly>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-98],rax
       mov       rcx,[rbp-98]
       call      qword ptr [7FF8AF9675E8]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor()
       mov       rax,[rbp-98]
       mov       [rbp-48],rax
       lea       rdx,[rbp-48]
       mov       rax,[rbp+10]
       mov       rcx,[rax+8]
       call      qword ptr [7FF8AFC8ED18]; Hl7.Fhir.Introspection.ModelInspector.<ForAssembly>g__importRecursively|2_3(System.Reflection.Assembly, <>c__DisplayClass2_1 ByRef)
       call      qword ptr [7FF8AFC8ED30]; Hl7.Fhir.Introspection.ModelInspector.<ForAssembly>g__getCqlTypes|2_2()
       mov       [rbp-0B0],rax
       mov       rcx,[rbp-0B0]
       mov       rdx,7FF8AFDE6180
       call      CORINFO_HELP_CLASSPROFILE32
       mov       rax,[rbp-0B0]
       mov       [rbp-120],rax
       mov       rcx,[rbp-120]
       mov       r11,7FF8AF570688
       call      qword ptr [r11]
       mov       [rbp-80],rax
       jmp       short M60_L03
M60_L02:
       mov       rcx,7FF8AFDE6288
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,[rbp-80]
       mov       [rbp-0B8],rax
       mov       rcx,[rbp-0B8]
       mov       rdx,7FF8AFDE6290
       call      CORINFO_HELP_CLASSPROFILE32
       mov       rax,[rbp-0B8]
       mov       [rbp-128],rax
       mov       rcx,[rbp-128]
       mov       r11,7FF8AF570698
       call      qword ptr [r11]
       mov       [rbp-88],rax
       mov       rcx,[rbp-40]
       mov       rdx,[rbp-88]
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFC8ED48]; Hl7.Fhir.Introspection.ModelInspector.ImportType(System.Type)
M60_L03:
       mov       eax,[rbp-0D0]
       dec       eax
       mov       [rbp-0D0],eax
       cmp       dword ptr [rbp-0D0],0
       jg        short M60_L04
       lea       rcx,[rbp-0D0]
       mov       edx,0C2
       call      CORINFO_HELP_PATCHPOINT
M60_L04:
       mov       rax,[rbp-80]
       mov       [rbp-0C0],rax
       mov       rcx,[rbp-0C0]
       mov       rdx,7FF8AFDE6398
       call      CORINFO_HELP_CLASSPROFILE32
       mov       rax,[rbp-0C0]
       mov       [rbp-130],rax
       mov       rcx,[rbp-130]
       mov       r11,7FF8AF570690
       call      qword ptr [r11]
       test      eax,eax
       jne       near ptr M60_L02
       mov       rcx,rsp
       call      M60_L05
       nop
       mov       rcx,7FF8AFDE65B4
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,[rbp-40]
       add       rsp,160
       pop       rbp
       ret
M60_L05:
       push      rbp
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+160]
       cmp       qword ptr [rbp-80],0
       je        short M60_L06
       mov       rcx,7FF8AFDE64A0
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,[rbp-80]
       mov       [rbp-0C8],rax
       mov       rcx,[rbp-0C8]
       mov       rdx,7FF8AFDE64A8
       call      CORINFO_HELP_CLASSPROFILE32
       mov       rax,[rbp-0C8]
       mov       [rbp-138],rax
       mov       rcx,[rbp-138]
       mov       r11,7FF8AF5706A0
       call      qword ptr [r11]
M60_L06:
       mov       rcx,7FF8AFDE65B0
       call      CORINFO_HELP_COUNTPROFILE32
       nop
       add       rsp,30
       pop       rbp
       ret
; Total bytes of code 1140
```
```assembly
; Hl7.Fhir.Specification.TypeSerializationInfoExtensions.GetTypeName(Hl7.Fhir.Specification.ITypeSerializationInfo)
       push      rsi
       push      rbx
       sub       rsp,48
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rsp+20],ymm4
       xor       eax,eax
       mov       [rsp+40],rax
       mov       rbx,rcx
       mov       rcx,rbx
       test      rcx,rcx
       je        short M61_L01
       mov       rdx,offset MT_Hl7.Fhir.Introspection.ClassMapping
       cmp       [rcx],rdx
       jne       short M61_L00
       xor       ecx,ecx
       jmp       short M61_L01
M61_L00:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Specification.IStructureDefinitionReference
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rcx,rax
M61_L01:
       test      rcx,rcx
       je        short M61_L03
       mov       rax,offset MT_Hl7.Fhir.Introspection.PropertyMapping+PocoTypeReferenceInfo
       cmp       [rcx],rax
       jne       short M61_L06
       mov       rax,[rcx+8]
M61_L02:
       add       rsp,48
       pop       rbx
       pop       rsi
       ret
M61_L03:
       mov       rcx,rbx
       test      rcx,rcx
       je        short M61_L04
       mov       rax,offset MT_Hl7.Fhir.Introspection.ClassMapping
       cmp       [rcx],rax
       jne       short M61_L07
M61_L04:
       test      rcx,rcx
       je        short M61_L09
       mov       rax,offset MT_Hl7.Fhir.Introspection.ClassMapping
       cmp       [rcx],rax
       jne       short M61_L08
       call      qword ptr [7FF8AFDE48C0]; Hl7.Fhir.Introspection.ClassMapping.Hl7.Fhir.Specification.IStructureDefinitionSummary.get_TypeName()
M61_L05:
       jmp       short M61_L02
M61_L06:
       mov       r11,7FF8AF572A18
       call      qword ptr [r11]
       jmp       short M61_L02
M61_L07:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Specification.IStructureDefinitionSummary
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rcx,rax
       jmp       short M61_L04
M61_L08:
       mov       r11,7FF8AF572A20
       call      qword ptr [r11]
       jmp       short M61_L05
M61_L09:
       lea       rcx,[rsp+20]
       mov       edx,34
       mov       r8d,1
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rsp+30]
       cmp       ecx,[rsp+40]
       ja        near ptr M61_L12
       mov       rdx,[rsp+38]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+40]
       sub       eax,ecx
       cmp       eax,34
       jb        short M61_L10
       mov       rcx,26D003A902C
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+20]
       vmovdqu   ymm2,ymmword ptr [rcx+40]
       vmovdqu   xmm3,xmmword ptr [rcx+58]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+20],ymm1
       vmovdqu   ymmword ptr [rdx+40],ymm2
       vmovdqu   xmmword ptr [rdx+58],xmm3
       mov       ecx,[rsp+30]
       add       ecx,34
       mov       [rsp+30],ecx
       jmp       short M61_L11
M61_L10:
       lea       rcx,[rsp+20]
       mov       rdx,26D003A9020
       call      qword ptr [7FF8B039CB58]
M61_L11:
       mov       rcx,rbx
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rsp+20]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       lea       rcx,[rsp+20]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rbx,rax
       mov       rcx,offset MT_System.NotSupportedException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8AF61F048]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
M61_L12:
       call      qword ptr [7FF8AF827798]
       int       3
; Total bytes of code 441
```
```assembly
; Hl7.Fhir.Introspection.ClassMapping.TryCreate(Hl7.Fhir.Introspection.ModelInspector, System.Type, Hl7.Fhir.Introspection.ClassMapping ByRef)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0C8
       vzeroupper
       lea       rbp,[rsp+100]
       xor       eax,eax
       mov       [rbp-0A8],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-0A0],ymm4
       vmovdqu   ymmword ptr [rbp-80],ymm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       mov       [rbp-40],rax
       mov       rdi,rcx
       mov       rbx,rdx
       mov       rsi,r8
       lea       rcx,[rbp-80]
       call      CORINFO_HELP_INIT_PINVOKE_FRAME
       mov       r14,rax
       mov       rcx,rsp
       mov       [rbp-68],rcx
       mov       rcx,rbp
       mov       [rbp-58],rcx
       mov       rcx,26D0038C988
       mov       rdx,rbx
       call      qword ptr [7FF8AF56A4C8]; System.RuntimeType.IsAssignableFrom(System.Type)
       test      eax,eax
       jne       near ptr M62_L67
       mov       rcx,26D3D801D00
       mov       rcx,[rcx]
       test      rcx,rcx
       je        near ptr M62_L38
       mov       rdx,rbx
       mov       r11,7FF8AF573170
       call      qword ptr [r11]
M62_L00:
       test      eax,eax
       jne       near ptr M62_L66
       xor       ecx,ecx
       mov       [rsi],rcx
       mov       r15,[rbx]
       mov       r13,offset MT_System.RuntimeType
       cmp       r15,r13
       jne       near ptr M62_L40
       mov       rcx,[rbx+18]
       test      cl,2
       jne       near ptr M62_L39
       mov       ecx,[rcx]
       and       ecx,80000030
       cmp       ecx,30
       sete      r12b
       movzx     r12d,r12b
M62_L01:
       movzx     r10d,r12b
M62_L02:
       test      r10d,r10d
       jne       near ptr M62_L64
       mov       rcx,rbx
       mov       rdx,26D0038CB08
       mov       r8d,1
       call      qword ptr [7FF8AFB15F38]; System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo, System.Type, Boolean)
       test      rax,rax
       je        short M62_L03
       cmp       dword ptr [rax+8],0
       jne       near ptr M62_L07
M62_L03:
       xor       edx,edx
M62_L04:
       mov       r12,rdx
       test      r12,r12
       je        short M62_L05
       mov       rcx,offset MT_Hl7.Fhir.Introspection.FhirTypeAttribute
       cmp       [r12],rcx
       jne       near ptr M62_L63
M62_L05:
       mov       [rbp-0B0],r12
       test      r12,r12
       je        near ptr M62_L65
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ClassMapping
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-0B8],rax
       mov       rcx,r12
       mov       rdx,rbx
       call      qword ptr [7FF8AFEA6208]; Hl7.Fhir.Introspection.ClassMapping.collectTypeName(Hl7.Fhir.Introspection.FhirTypeAttribute, System.Type)
       mov       [rbp-0D0],rax
       mov       rcx,26D3D801D78
       mov       rdx,[rcx]
       test      rdx,rdx
       je        near ptr M62_L42
M62_L06:
       mov       r8,[rbp-0B8]
       lea       rcx,[r8+8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-0B8]
       lea       rcx,[rax+10]
       mov       [rbp+10],rdi
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-0B8]
       lea       rcx,[rax+18]
       mov       rdx,[rbp-0D0]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-0B8]
       lea       rcx,[rax+20]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,26D3D801D88
       mov       rdx,[rcx]
       mov       rax,[rbp-0B8]
       lea       rcx,[rax+38]
       call      CORINFO_HELP_ASSIGN_REF
       cmp       r15,r13
       jne       near ptr M62_L44
       mov       rcx,[rbx+18]
       test      cl,2
       jne       near ptr M62_L43
       test      dword ptr [rcx],80000000
       jne       short M62_L08
       test      byte ptr [rcx],30
       setne     al
       movzx     eax,al
       jmp       short M62_L09
M62_L07:
       mov       rcx,[rax+10]
       cmp       dword ptr [rax+8],1
       jne       near ptr M62_L41
       mov       rdx,rcx
       jmp       near ptr M62_L04
M62_L08:
       xor       eax,eax
M62_L09:
       movzx     edx,al
M62_L10:
       mov       [rbp-0C0],rsi
       mov       r10,[rbp-0B8]
       mov       r9,r10
       mov       [rbp-0C8],r9
       test      edx,edx
       je        near ptr M62_L56
       cmp       r15,r13
       jne       near ptr M62_L47
       mov       rcx,[rbx+18]
       test      cl,2
       jne       near ptr M62_L45
       test      dword ptr [rcx],80000000
       jne       short M62_L11
       test      byte ptr [rcx],30
       setne     r11b
       movzx     r11d,r11b
       jmp       short M62_L12
M62_L11:
       xor       r11d,r11d
M62_L12:
       test      r11b,r11b
       je        near ptr M62_L46
       mov       rcx,[rbx+10]
       test      rcx,rcx
       je        short M62_L15
       mov       rcx,[rcx]
       test      rcx,rcx
       je        short M62_L15
M62_L13:
       mov       rax,[rcx+88]
       test      rax,rax
       je        short M62_L16
M62_L14:
       mov       rcx,26D0038CAB8
       cmp       rax,rcx
       jne       near ptr M62_L56
       cmp       r15,r13
       jne       near ptr M62_L55
       mov       rcx,[rbx+18]
       mov       rax,rcx
       test      al,2
       jne       near ptr M62_L48
       test      dword ptr [rax],80000000
       jne       short M62_L17
       test      byte ptr [rax],30
       setne     al
       movzx     eax,al
       jmp       short M62_L18
M62_L15:
       mov       rcx,rbx
       call      qword ptr [7FF8AF82C498]; System.RuntimeType.InitializeCache()
       mov       rcx,rax
       jmp       short M62_L13
M62_L16:
       call      qword ptr [7FF8AF96C6A8]; System.RuntimeType+RuntimeTypeCache.<GetGenericTypeDefinition>g__CacheGenericDefinition|46_0()
       jmp       short M62_L14
M62_L17:
       xor       eax,eax
M62_L18:
       test      al,al
       je        near ptr M62_L54
       test      cl,2
       jne       near ptr M62_L49
       mov       ecx,[rcx]
       and       ecx,80000030
       cmp       ecx,30
       sete      dl
       movzx     edx,dl
M62_L19:
       test      dl,dl
       jne       near ptr M62_L54
       mov       [rbp+18],rbx
       mov       r15,rbx
M62_L20:
       cmp       [r15],r13
       jne       near ptr M62_L50
       mov       rcx,r15
       call      System.RuntimeTypeHandle.GetCorElementType(System.RuntimeType)
       cmp       eax,1D
       ja        short M62_L21
       mov       ecx,1FEF7FFF
       bt        ecx,eax
       jae       near ptr M62_L34
M62_L21:
       cmp       eax,10
       sete      al
       movzx     eax,al
M62_L22:
       test      eax,eax
       jne       near ptr M62_L53
       cmp       [r15],r13
       jne       near ptr M62_L51
M62_L23:
       xor       ecx,ecx
       mov       [rbp-40],rcx
       test      r15,r15
       je        near ptr M62_L52
       mov       [rbp-48],r15
       mov       rcx,[rbp-48]
       test      rcx,rcx
       je        near ptr M62_L35
       mov       rcx,[rcx+18]
M62_L24:
       lea       rdx,[rbp-48]
       mov       [rbp-98],rdx
       mov       [rbp-90],rcx
       lea       rcx,[rbp-98]
       lea       rdx,[rbp-40]
       xor       r8d,r8d
       mov       rax,7FF8AF7687C0
       mov       [rbp-70],rax
       lea       rax,[M62_L25]
       mov       [rbp-60],rax
       lea       rax,[rbp-80]
       mov       [r14+8],rax
       mov       byte ptr [r14+4],0
       mov       rax,7FF90F13ADD0
       call      rax
M62_L25:
       mov       byte ptr [r14+4],1
       cmp       dword ptr [7FF90F57C744],0
       je        short M62_L26
       call      qword ptr [7FF90F56A418]; CORINFO_HELP_STOP_FOR_GC
M62_L26:
       mov       rcx,[rbp-78]
       mov       [r14+8],rcx
       mov       rcx,[rbp-40]
       xor       edx,edx
       mov       [rbp-40],rdx
       mov       rdx,26D3D8002F0
       test      rcx,rcx
       cmove     rcx,[rdx]
       mov       rax,rcx
M62_L27:
       mov       rbx,[rbp-0C0]
       mov       rdi,[rbp-0C8]
       cmp       dword ptr [rax+8],0
       jbe       near ptr M62_L68
       mov       rdx,[rax+10]
M62_L28:
       lea       rcx,[rdi+28]
       call      CORINFO_HELP_ASSIGN_REF
       mov       r12,[rbp-0B0]
       movzx     ecx,byte ptr [r12+0C]
       mov       [rdi+59],cl
       mov       rdx,[r12+18]
       lea       rcx,[rdi+30]
       call      CORINFO_HELP_ASSIGN_REF
       mov       r15,[rbp+10]
       mov       esi,[r15+28]
       mov       rcx,offset MT_Hl7.Fhir.Utility.ReflectionHelper+<>c__DisplayClass11_0<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       [r14+8],esi
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,[rbp+18]
       mov       rdx,26D0038CBB0
       mov       r8d,1
       call      qword ptr [7FF8AFB15F38]; System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo, System.Type, Boolean)
       mov       rdx,rax
       mov       rcx,offset MT_System.Collections.Generic.IEnumerable<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r15,rax
       lea       rcx,[rsi+8]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,offset Hl7.Fhir.Utility.ReflectionHelper+<>c__DisplayClass11_0`1[[System.__Canon, System.Private.CoreLib]].<GetFhirModelAttributes>g__isRelevant|1(Hl7.Fhir.Introspection.FhirModelAttribute)
       mov       [rsi+18],rdx
       test      r15,r15
       je        near ptr M62_L62
       mov       rdx,r15
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M62_L58
       mov       rdx,r15
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ValidatingFhirModelAttribute[]
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       mov       r14,rax
       test      r14,r14
       jne       near ptr M62_L37
       mov       rdx,r15
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r14,rax
       test      r14,r14
       je        near ptr M62_L36
       mov       rcx,offset MT_System.Linq.Enumerable+ListWhereIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r12+10],eax
       lea       rcx,[r12+18]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+20]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
M62_L29:
       mov       rcx,26D3D801D98
       mov       r14,[rcx]
       test      r14,r14
       je        near ptr M62_L59
M62_L30:
       mov       rcx,offset MT_System.Linq.Enumerable+OrderedIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute, Hl7.Fhir.Specification.FhirRelease>
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r15+10],eax
       lea       rcx,[r15+18]
       mov       rdx,r12
       call      CORINFO_HELP_ASSIGN_REF
       test      r12,r12
       je        near ptr M62_L62
       test      r14,r14
       je        near ptr M62_L61
       xor       ecx,ecx
       mov       [r15+20],rcx
       lea       rcx,[r15+28]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,26D3D801CF8
       mov       rdx,[rcx]
       lea       rcx,[r15+30]
       call      CORINFO_HELP_ASSIGN_REF
       mov       byte ptr [r15+48],0
       mov       rdx,[r15+18]
       mov       rcx,7FF8AFEC5D80
       call      qword ptr [7FF8AF967858]; System.Linq.Enumerable.ToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       mov       r12,rax
       cmp       dword ptr [r12+8],1
       jg        near ptr M62_L60
       mov       rdx,r12
M62_L31:
       lea       rcx,[rdi+38]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,rbx
       mov       rdx,rdi
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
M62_L32:
       mov       r10d,1
M62_L33:
       movzx     eax,r10b
       add       rsp,0C8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M62_L34:
       mov       eax,1
       jmp       near ptr M62_L22
M62_L35:
       xor       ecx,ecx
       jmp       near ptr M62_L24
M62_L36:
       mov       rcx,offset MT_System.Linq.Enumerable+IEnumerableWhereIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r12+10],eax
       lea       rcx,[r12+18]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+20]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M62_L29
M62_L37:
       cmp       dword ptr [r14+8],0
       jne       near ptr M62_L57
       mov       r8,26D3D801D88
       mov       r12,[r8]
       jmp       near ptr M62_L29
M62_L38:
       mov       r8,rbx
       mov       rcx,7FF8B074AC60
       xor       edx,edx
       xor       r9d,r9d
       call      qword ptr [7FF8B050C7E0]
       jmp       near ptr M62_L00
M62_L39:
       xor       r12d,r12d
       jmp       near ptr M62_L01
M62_L40:
       mov       rcx,rbx
       mov       rax,[r15+60]
       call      qword ptr [rax+10]
       mov       r10d,eax
       jmp       near ptr M62_L02
M62_L41:
       call      qword ptr [7FF8B050CA80]
       mov       rcx,rax
       call      CORINFO_HELP_THROW
       int       3
M62_L42:
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ClassMapping+PropertyMapper
       call      CORINFO_HELP_NEWSFAST
       mov       rdx,rax
       mov       [rbp-0D8],rdx
       mov       rcx,rdx
       xor       edx,edx
       mov       r8,offset Hl7.Fhir.Introspection.ClassMapping.DefaultPropertyMapper(Hl7.Fhir.Introspection.ClassMapping)
       mov       r9,7FF8AF56D140
       call      qword ptr [7FF8AF616EF8]; System.MulticastDelegate.CtorOpened(System.Object, IntPtr, IntPtr)
       mov       rcx,26D3D801D78
       mov       rdx,[rbp-0D8]
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,[rbp-0D8]
       jmp       near ptr M62_L06
M62_L43:
       xor       eax,eax
       jmp       near ptr M62_L09
M62_L44:
       mov       rcx,rbx
       mov       rax,[r15+60]
       call      qword ptr [rax+8]
       mov       edx,eax
       jmp       near ptr M62_L10
M62_L45:
       xor       r11d,r11d
       jmp       near ptr M62_L12
M62_L46:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B0507870]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M62_L47:
       mov       rcx,rbx
       mov       rax,[r15+68]
       call      qword ptr [rax+18]
       jmp       near ptr M62_L14
M62_L48:
       xor       eax,eax
       jmp       near ptr M62_L18
M62_L49:
       xor       edx,edx
       jmp       near ptr M62_L19
M62_L50:
       mov       rcx,r15
       mov       rax,[r15]
       mov       rax,[rax+68]
       call      qword ptr [rax]
       jmp       near ptr M62_L22
M62_L51:
       mov       rcx,r15
       mov       rax,[r15]
       mov       rax,[rax+98]
       call      qword ptr [rax+8]
       mov       r15,rax
       jmp       near ptr M62_L23
M62_L52:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       call      qword ptr [7FF8B0507258]
       mov       r8,rax
       mov       rcx,r14
       xor       edx,edx
       call      qword ptr [7FF8B0507270]
       mov       rcx,r14
       call      CORINFO_HELP_THROW
       int       3
M62_L53:
       mov       rbx,[rbp+18]
       mov       rcx,r15
       mov       rax,[r15]
       mov       rax,[rax+68]
       call      qword ptr [rax+8]
       mov       r15,rax
       mov       [rbp+18],rbx
       jmp       near ptr M62_L20
M62_L54:
       mov       rcx,26D3D8002F0
       mov       rax,[rcx]
       mov       [rbp+18],rbx
       jmp       near ptr M62_L27
M62_L55:
       mov       rcx,rbx
       mov       rax,[r15+68]
       call      qword ptr [rax+20]
       mov       [rbp+18],rbx
       jmp       near ptr M62_L27
M62_L56:
       mov       r10,[rbp-0B8]
       mov       r15,r10
       xor       edx,edx
       mov       [rbp+18],rbx
       mov       rbx,rsi
       mov       rdi,r15
       jmp       near ptr M62_L28
M62_L57:
       mov       rcx,offset MT_System.Linq.Enumerable+ArrayWhereIterator<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       mov       rcx,r12
       mov       rdx,r14
       mov       r8,rsi
       call      qword ptr [7FF8B050EA48]
       jmp       near ptr M62_L29
M62_L58:
       mov       rcx,rax
       mov       rdx,rsi
       mov       rax,[rax]
       mov       rax,[rax+50]
       call      qword ptr [rax+8]
       mov       r12,rax
       jmp       near ptr M62_L29
M62_L59:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.ValidatingFhirModelAttribute, Hl7.Fhir.Specification.FhirRelease>
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rdx,26D3D801D90
       mov       rdx,[rdx]
       mov       rcx,r14
       mov       r8,offset Hl7.Fhir.Utility.ReflectionHelper+<>c__11`1[[System.__Canon, System.Private.CoreLib]].<GetFhirModelAttributes>b__11_0(System.__Canon)
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D801D98
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M62_L30
M62_L60:
       mov       edx,[r12+8]
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ValidatingFhirModelAttribute[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rsi,rax
       lea       r8,[rsi+10]
       mov       edx,[rsi+8]
       mov       [rbp-0A8],r8
       mov       [rbp-0A0],edx
       lea       r8,[rbp-0A8]
       mov       rdx,r12
       mov       rcx,r15
       call      qword ptr [7FF8AF96D740]; System.Linq.Enumerable+OrderedIterator`1[[System.__Canon, System.Private.CoreLib]].Fill(System.__Canon[], System.Span`1<System.__Canon>)
       mov       rdx,rsi
       jmp       near ptr M62_L31
M62_L61:
       mov       ecx,9
       call      qword ptr [7FF8AF61F738]
       int       3
M62_L62:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M62_L63:
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M62_L64:
       mov       rcx,offset MT_System.Object[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rsi,rax
       mov       rcx,rbx
       mov       rax,[r15+40]
       call      qword ptr [rax+30]
       lea       rcx,[rsi+10]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M62_L65:
       xor       r10d,r10d
       jmp       near ptr M62_L33
M62_L66:
       mov       rcx,rbx
       mov       rdx,rdi
       call      qword ptr [7FF8AFEA61D8]
       mov       rdx,rax
       mov       rcx,rsi
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       jmp       near ptr M62_L32
M62_L67:
       mov       rcx,rbx
       mov       rdx,rdi
       call      qword ptr [7FF8AFEA61C0]; Hl7.Fhir.Introspection.ClassMapping.buildCqlClassMapping(System.Type, Hl7.Fhir.Introspection.ModelInspector)
       mov       rdx,rax
       mov       rcx,rsi
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       jmp       near ptr M62_L32
M62_L68:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 2517
```
```assembly
; System.SpanHelpers.SequenceEqual(Byte ByRef, Byte ByRef, UIntPtr)
       cmp       r8,8
       jb        near ptr M63_L06
       cmp       rcx,rdx
       je        short M63_L04
       cmp       r8,20
       jae       near ptr M63_L11
       cmp       r8,10
       jae       short M63_L01
       add       r8,0FFFFFFFFFFFFFFF8
       mov       rax,[rcx]
       sub       rax,[rdx]
       mov       rcx,[rcx+r8]
       sub       rcx,[rdx+r8]
       or        rax,rcx
       sete      al
       movzx     eax,al
M63_L00:
       vzeroupper
       ret
M63_L01:
       xor       eax,eax
       add       r8,0FFFFFFFFFFFFFFF0
       je        short M63_L03
M63_L02:
       vmovups   xmm0,[rcx+rax]
       vpcmpeqb  xmm0,xmm0,[rdx+rax]
       vpmovmskb r10d,xmm0
       cmp       r10d,0FFFF
       jne       short M63_L05
       add       rax,10
       cmp       r8,rax
       ja        short M63_L02
M63_L03:
       vmovups   xmm0,[rcx+r8]
       vpcmpeqb  xmm0,xmm0,[rdx+r8]
       vpmovmskb eax,xmm0
       cmp       eax,0FFFF
       jne       short M63_L05
M63_L04:
       mov       eax,1
       vzeroupper
       ret
M63_L05:
       xor       eax,eax
       vzeroupper
       ret
M63_L06:
       cmp       r8,4
       jb        short M63_L07
       add       r8,0FFFFFFFFFFFFFFFC
       mov       eax,[rcx]
       sub       eax,[rdx]
       mov       ecx,[rcx+r8]
       sub       ecx,[rdx+r8]
       or        eax,ecx
       sete      al
       movzx     eax,al
       jmp       short M63_L00
M63_L07:
       xor       r10d,r10d
       mov       r9,r8
       and       r9,2
       jne       short M63_L10
M63_L08:
       test      r8b,1
       jne       short M63_L14
M63_L09:
       test      r10d,r10d
       sete      al
       movzx     eax,al
       jmp       near ptr M63_L00
M63_L10:
       movzx     r10d,word ptr [rcx]
       movzx     eax,word ptr [rdx]
       sub       r10d,eax
       jmp       short M63_L08
M63_L11:
       xor       r9d,r9d
       lea       r10,[r8-20]
       test      r10,r10
       je        short M63_L13
M63_L12:
       vmovups   ymm0,[rcx+r9]
       vpcmpeqb  ymm0,ymm0,[rdx+r9]
       vpmovmskb eax,ymm0
       cmp       eax,0FFFFFFFF
       jne       short M63_L05
       add       r9,20
       cmp       r10,r9
       ja        short M63_L12
M63_L13:
       vmovups   ymm0,[rcx+r10]
       vpcmpeqb  ymm0,ymm0,[rdx+r10]
       vpmovmskb ecx,ymm0
       cmp       ecx,0FFFFFFFF
       jne       near ptr M63_L05
       jmp       near ptr M63_L04
M63_L14:
       movzx     eax,byte ptr [rcx+r9]
       movzx     ecx,byte ptr [rdx+r9]
       sub       eax,ecx
       or        r10d,eax
       jmp       short M63_L09
; Total bytes of code 305
```
```assembly
; System.String.StartsWith(System.String, System.StringComparison)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       xor       eax,eax
       mov       [rsp+28],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rsp+30],xmm4
       mov       [rsp+40],rax
       mov       rsi,rcx
       mov       rbx,rdx
       mov       edi,r8d
M64_L00:
       test      rbx,rbx
       je        near ptr M64_L11
       cmp       rsi,rbx
       je        near ptr M64_L09
       mov       ebp,[rbx+8]
       test      ebp,ebp
       je        near ptr M64_L08
       cmp       edi,5
       ja        near ptr M64_L12
       mov       ecx,edi
       lea       rdx,[7FF8B05FFA88]
       mov       edx,[rdx+rcx*4]
       lea       r8,[M64_L00]
       add       rdx,r8
       jmp       rdx
       cmp       [rsi+8],ebp
       jge       short M64_L02
M64_L01:
       xor       eax,eax
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M64_L02:
       lea       rcx,[rsi+0C]
       lea       rdx,[rbx+0C]
       mov       r8d,ebp
       cmp       r8d,8
       jl        short M64_L05
       cmp       r8d,10
       jl        short M64_L04
       call      qword ptr [7FF8B0505D88]
M64_L03:
       jmp       near ptr M64_L07
M64_L04:
       call      qword ptr [7FF8AFEA59E0]; System.Globalization.Ordinal.EqualsIgnoreCase_Vector[[System.Runtime.Intrinsics.Vector128`1[[System.UInt16, System.Private.CoreLib]], System.Private.CoreLib]](Char ByRef, Char ByRef, Int32)
       jmp       short M64_L03
M64_L05:
       call      qword ptr [7FF8AFEAFF18]; System.Globalization.Ordinal.EqualsIgnoreCase_Scalar(Char ByRef, Char ByRef, Int32)
       jmp       short M64_L03
       cmp       [rsi+8],ebp
       jl        short M64_L01
       movzx     r8d,word ptr [rsi+0C]
       cmp       r8w,[rbx+0C]
       jne       short M64_L01
       cmp       ebp,1
       je        near ptr M64_L10
       lea       rcx,[rsi+0C]
       mov       r8d,ebp
       add       r8,r8
       lea       rdx,[rbx+0C]
       call      qword ptr [7FF8AF61C090]; System.SpanHelpers.SequenceEqual(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       near ptr M64_L07
       test      byte ptr [7FF8AF979D00],1
       je        near ptr M64_L13
M64_L06:
       mov       rdx,26D3D804640
       mov       rcx,[rdx]
       cmp       [rcx],cl
       test      rsi,rsi
       je        near ptr M64_L14
       lea       rdx,[rsi+0C]
       mov       r8d,[rsi+8]
       lea       r9,[rbx+0C]
       mov       eax,ebp
       mov       [rsp+38],rdx
       mov       [rsp+40],r8d
       mov       [rsp+28],r9
       mov       [rsp+30],eax
       lea       rdx,[rsp+38]
       lea       r8,[rsp+28]
       mov       r9d,edi
       and       r9d,1
       call      qword ptr [7FF8AF965908]; System.Globalization.CompareInfo.IsPrefix(System.ReadOnlySpan`1<Char>, System.ReadOnlySpan`1<Char>, System.Globalization.CompareOptions)
       jmp       short M64_L07
       call      qword ptr [7FF8AF61D080]; System.Globalization.CultureInfo.get_CurrentCulture()
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+48]
       call      qword ptr [rax+30]
       cmp       [rax],al
       test      rsi,rsi
       je        near ptr M64_L14
       lea       rdx,[rsi+0C]
       mov       r8d,[rsi+8]
       lea       r9,[rbx+0C]
       mov       ecx,ebp
       mov       [rsp+38],rdx
       mov       [rsp+40],r8d
       mov       [rsp+28],r9
       mov       [rsp+30],ecx
       lea       rdx,[rsp+38]
       lea       r8,[rsp+28]
       mov       r9d,edi
       and       r9d,1
       mov       rcx,rax
       call      qword ptr [7FF8AF965908]; System.Globalization.CompareInfo.IsPrefix(System.ReadOnlySpan`1<Char>, System.ReadOnlySpan`1<Char>, System.Globalization.CompareOptions)
M64_L07:
       movzx     eax,al
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M64_L08:
       cmp       edi,5
       jbe       short M64_L10
       jmp       near ptr M64_L15
M64_L09:
       cmp       edi,5
       ja        near ptr M64_L15
M64_L10:
       mov       eax,1
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M64_L11:
       mov       ecx,371
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B0506550]
       int       3
M64_L12:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B0507408]
       mov       rsi,rax
       mov       ecx,1785
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF8AF966F28]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M64_L13:
       mov       rcx,offset MT_System.Globalization.CompareInfo
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       jmp       near ptr M64_L06
M64_L14:
       mov       ecx,27
       call      qword ptr [7FF8AF61FB28]
       int       3
M64_L15:
       mov       ecx,1B
       mov       edx,29
       call      qword ptr [7FF8B0507420]
       int       3
; Total bytes of code 597
```
```assembly
; System.RuntimeType.IsAssignableFrom(System.Type)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rsi,rcx
       mov       rbx,rdx
       test      rbx,rbx
       je        near ptr M65_L06
       cmp       rbx,rsi
       je        short M65_L01
       mov       rcx,offset MT_System.RuntimeType
       cmp       [rbx],rcx
       jne       short M65_L02
       mov       rcx,rbx
M65_L00:
       test      rcx,rcx
       je        short M65_L03
       mov       rdx,offset MT_System.RuntimeType
       cmp       [rcx],rdx
       jne       short M65_L03
       mov       rdx,rsi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       near ptr System.RuntimeTypeHandle.CanCastTo(System.RuntimeType, System.RuntimeType)
M65_L01:
       mov       eax,1
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M65_L02:
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+58]
       call      qword ptr [rax]
       mov       rcx,rax
       jmp       short M65_L00
M65_L03:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Reflection.Emit.TypeBuilder
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M65_L06
       mov       rcx,rbx
       mov       rdx,rsi
       mov       rax,[rbx]
       mov       rax,[rax+0B0]
       call      qword ptr [rax+18]
       test      eax,eax
       jne       short M65_L01
       mov       rcx,rsi
       call      qword ptr [7FF8AF82C918]; System.RuntimeType.get_IsInterface()
       test      eax,eax
       jne       short M65_L05
       cmp       [rsi],sil
       mov       rcx,rsi
       call      System.RuntimeTypeHandle.IsGenericVariable(System.RuntimeType)
       test      eax,eax
       je        short M65_L06
       mov       rcx,rsi
       call      qword ptr [7FF8AF56A2B8]
       mov       rsi,rax
       xor       edi,edi
M65_L04:
       cmp       [rsi+8],edi
       jle       short M65_L01
       mov       rcx,[rsi+rdi*8+10]
       mov       rdx,rbx
       mov       rax,[rcx]
       mov       rax,[rax+0B0]
       call      qword ptr [rax+20]
       test      eax,eax
       je        short M65_L06
       inc       edi
       jmp       short M65_L04
M65_L05:
       mov       rcx,rbx
       mov       rdx,rsi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8B045E898]
M65_L06:
       xor       eax,eax
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 261
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.determineBestDynamicMappingForElement(Hl7.Fhir.ElementModel.ITypedElement)
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rsp+30],xmm4
       xor       eax,eax
       mov       [rsp+40],rax
       mov       rbx,rcx
       mov       [rsp+38],rdx
       mov       [rsp+40],rbx
       mov       rsi,[rsp+38]
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rsi],rcx
       jne       near ptr M66_L15
       mov       rcx,offset MT_System.Func<System.Object>
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       lea       rbp,[rsi+38]
       lea       r14,[rsi+50]
       lea       rcx,[rdi+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
       mov       [rdi+18],rdx
       cmp       byte ptr [r14],0
       je        near ptr M66_L10
       mov       r15,[rbp]
M66_L00:
       test      r15,r15
       jne       near ptr M66_L27
       mov       rcx,[rsp+38]
       mov       rdx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rcx],rdx
       jne       near ptr M66_L16
       mov       rdi,[rcx+10]
M66_L01:
       test      rdi,rdi
       je        short M66_L02
       cmp       dword ptr [rdi+8],0
       jbe       near ptr M66_L28
       movzx     ecx,word ptr [rdi+0C]
       cmp       ecx,100
       jae       near ptr M66_L17
       mov       edx,ecx
       mov       rcx,7FF90A792F18
       test      byte ptr [rdx+rcx],20
       jne       near ptr M66_L27
M66_L02:
       mov       rdx,[rsp+38]
       mov       rcx,offset MT_Hl7.Fhir.Utility.IAnnotated
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       je        near ptr M66_L21
       mov       rcx,rax
       mov       r11,7FF8AF573338
       mov       rdx,26D00396938
       call      qword ptr [r11]
       mov       rsi,rax
       test      rsi,rsi
       je        near ptr M66_L20
       mov       rdx,rsi
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Object>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rbx,rax
       test      rbx,rbx
       jne       near ptr M66_L11
       lea       r8,[rsp+30]
       mov       rdx,rsi
       mov       rcx,7FF8B0076700
       call      qword ptr [7FF8AFEAFE10]; System.Linq.Enumerable.TryGetFirstNonIterator[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
M66_L03:
       mov       rdx,rax
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.IResourceTypeSupplier
       call      qword ptr [7FF8AF82E268]; System.Runtime.CompilerServices.CastHelpers.ChkCastInterface(Void*, System.Object)
M66_L04:
       test      rax,rax
       je        near ptr M66_L23
       mov       rdx,offset MT_Hl7.Fhir.Serialization.FhirJsonNode
       cmp       [rax],rdx
       jne       near ptr M66_L22
       mov       rcx,[rax+18]
       test      rcx,rcx
       jne       near ptr M66_L13
M66_L05:
       xor       edx,edx
M66_L06:
       mov       rcx,offset MT_Newtonsoft.Json.Linq.JValue
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M66_L14
M66_L07:
       xor       esi,esi
M66_L08:
       test      rsi,rsi
       je        near ptr M66_L23
M66_L09:
       call      qword ptr [7FF8B017DE48]; Hl7.Fhir.Introspection.ClassMapping.get_DynamicResource()
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M66_L10:
       mov       [rsp+20],rdi
       mov       rdx,rbp
       mov       r8,r14
       mov       rcx,7FF8B0245968
       mov       r9,26D3D801FD0
       call      qword ptr [7FF8B017DF50]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Boolean ByRef, System.Object ByRef, System.Func`1<System.__Canon>)
       mov       r15,rax
       jmp       near ptr M66_L00
M66_L11:
       mov       rcx,offset MT_System.Linq.Enumerable+IListSkipTakeIterator<Newtonsoft.Json.Linq.JProperty>
       cmp       [rbx],rcx
       jne       near ptr M66_L19
       mov       rcx,[rbx+18]
       mov       r11,7FF8AF573340
       call      qword ptr [r11]
       cmp       eax,[rbx+20]
       jg        near ptr M66_L18
       xor       edx,edx
       mov       [rsp+30],edx
       xor       eax,eax
M66_L12:
       jmp       near ptr M66_L03
M66_L13:
       mov       rdx,[rax+20]
       call      qword ptr [7FF8AFB87978]; Hl7.Fhir.Serialization.JTokenExtensions.GetResourceTypePropertyFromObject(Newtonsoft.Json.Linq.JObject, System.String)
       test      rax,rax
       je        near ptr M66_L05
       mov       rcx,[rax+50]
       mov       rdx,[rcx+8]
       jmp       near ptr M66_L06
M66_L14:
       mov       rsi,[rax+28]
       test      rsi,rsi
       je        near ptr M66_L07
       mov       rcx,offset MT_System.String
       cmp       [rsi],rcx
       jne       near ptr M66_L07
       jmp       near ptr M66_L08
M66_L15:
       mov       rcx,rsi
       mov       r11,7FF8AF573310
       call      qword ptr [r11]
       mov       r15,rax
       jmp       near ptr M66_L00
M66_L16:
       mov       r11,7FF8AF573318
       call      qword ptr [r11]
       mov       rdi,rax
       jmp       near ptr M66_L01
M66_L17:
       call      qword ptr [7FF8B05077F8]
       cmp       eax,1
       jne       near ptr M66_L02
       jmp       near ptr M66_L27
M66_L18:
       mov       dword ptr [rsp+30],1
       mov       rcx,[rbx+18]
       mov       edx,[rbx+20]
       mov       r11,7FF8AF573348
       call      qword ptr [r11]
       jmp       near ptr M66_L12
M66_L19:
       lea       rdx,[rsp+30]
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+48]
       call      qword ptr [rax+10]
       jmp       near ptr M66_L12
M66_L20:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M66_L21:
       xor       eax,eax
       jmp       near ptr M66_L04
M66_L22:
       mov       rcx,rax
       mov       r11,7FF8AF573320
       call      qword ptr [r11]
       mov       rsi,rax
       jmp       near ptr M66_L08
M66_L23:
       mov       rcx,[rsp+38]
       mov       r11,7FF8AF573328
       call      qword ptr [r11]
       test      rax,rax
       jne       short M66_L24
       xor       ebx,ebx
       xor       esi,esi
       jmp       short M66_L25
M66_L24:
       mov       rcx,rax
       mov       r11,7FF8AF573330
       call      qword ptr [r11]
       movzx     esi,al
       mov       ebx,1
M66_L25:
       test      bl,bl
       je        short M66_L26
       test      sil,sil
       jne       near ptr M66_L09
M66_L26:
       call      qword ptr [7FF8B017DE60]
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M66_L27:
       lea       rdx,[rsp+38]
       mov       rcx,rbx
       call      qword ptr [7FF8B017DE30]
       nop
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M66_L28:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 917
```
```assembly
; System.String.Concat(System.String, System.String, System.String)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rbx,rbx
       je        near ptr M67_L03
       mov       ebp,[rbx+8]
       test      ebp,ebp
       je        near ptr M67_L03
       test      rsi,rsi
       je        near ptr M67_L02
       mov       r14d,[rsi+8]
       test      r14d,r14d
       je        near ptr M67_L02
       test      rdi,rdi
       je        near ptr M67_L01
       mov       r15d,[rdi+8]
       test      r15d,r15d
       je        near ptr M67_L01
       mov       r13d,ebp
       mov       ecx,r14d
       add       rcx,r13
       mov       eax,r15d
       add       rcx,rax
       cmp       rcx,7FFFFFFF
       jg        short M67_L00
       call      00007FF8AF6124D8
       mov       r12,rax
       cmp       [r12],r12b
       lea       rax,[r12+0C]
       mov       [rsp+20],rax
       mov       rcx,rax
       mov       r8d,ebp
       add       r8,r8
       lea       rdx,[rbx+0C]
       call      qword ptr [7FF8AF6157B8]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rbx,[rsp+20]
       lea       rcx,[rbx+r13*2]
       mov       r8d,r14d
       add       r8,r8
       lea       rdx,[rsi+0C]
       call      qword ptr [7FF8AF6157B8]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       add       ebp,r14d
       movsxd    rcx,ebp
       lea       rcx,[rbx+rcx*2]
       mov       r8d,r15d
       add       r8,r8
       lea       rdx,[rdi+0C]
       call      qword ptr [7FF8AF6157B8]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rax,r12
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M67_L00:
       call      qword ptr [7FF8B05070A8]
       int       3
M67_L01:
       mov       rcx,rbx
       mov       rdx,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       jmp       qword ptr [7FF8AF61D788]; System.String.Concat(System.String, System.String)
M67_L02:
       mov       rcx,rbx
       mov       rdx,rdi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       jmp       qword ptr [7FF8AF61D788]; System.String.Concat(System.String, System.String)
M67_L03:
       mov       rcx,rsi
       mov       rdx,rdi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       jmp       qword ptr [7FF8AF61D788]; System.String.Concat(System.String, System.String)
; Total bytes of code 316
```
```assembly
; System.String.Substring(Int32)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rsi,rcx
       mov       ebx,edx
       test      ebx,ebx
       je        short M68_L01
       mov       ecx,[rsi+8]
       mov       r8d,ecx
       sub       r8d,ebx
       je        short M68_L00
       cmp       ecx,ebx
       jb        short M68_L02
       mov       edi,r8d
       mov       ecx,r8d
       call      qword ptr [7FF90B382D98]
       mov       rbp,rax
       mov       edx,ebx
       lea       rdx,[rsi+rdx*2+0C]
       cmp       [rbp],bpl
       lea       rcx,[rbp+0C]
       lea       r8,[rdi+rdi]
       call      qword ptr [7FF90B384C58]; Precode of System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rax,rbp
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M68_L00:
       mov       rax,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       mov       rax,[rax]
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M68_L01:
       mov       rax,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M68_L02:
       mov       rcx,rsi
       mov       edx,ebx
       call      qword ptr [7FF90B3830E0]
       int       3
; Total bytes of code 127
```
```assembly
; Hl7.Fhir.Introspection.ModelInspector.FindClassMapping(System.String)
       sub       rsp,28
       xor       eax,eax
       mov       [rsp+20],rax
       mov       r8,[rcx+20]
       mov       rcx,[r8+8]
       test      rcx,rcx
       je        short M69_L00
       lea       r8,[rsp+20]
       mov       r11,7FF8AF572A88
       call      qword ptr [r11]
       xor       ecx,ecx
       test      eax,eax
       mov       rax,rcx
       cmovne    rax,[rsp+20]
       add       rsp,28
       ret
M69_L00:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
; Total bytes of code 72
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.getClassMapping(System.Type)
       push      rsi
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+30],rax
       mov       [rsp+28],rax
       mov       rsi,rcx
       mov       rbx,rdx
       mov       r8,[rsi+8]
       mov       r8,[r8+20]
       mov       rcx,[r8+18]
       test      rcx,rcx
       je        near ptr M70_L03
       lea       r8,[rsp+28]
       mov       rdx,rbx
       mov       r11,7FF8AF572F58
       call      qword ptr [r11]
       xor       ecx,ecx
       test      eax,eax
       cmovne    rcx,[rsp+28]
       mov       rax,rcx
       xor       ecx,ecx
       mov       [rsp+28],rcx
       test      rax,rax
       je        short M70_L01
M70_L00:
       add       rsp,38
       pop       rbx
       pop       rsi
       ret
M70_L01:
       mov       rcx,[rsi+8]
       lea       r8,[rsp+30]
       mov       rdx,rbx
       call      qword ptr [7FF8AFEA6070]; Hl7.Fhir.Introspection.ClassMapping.TryCreate(Hl7.Fhir.Introspection.ModelInspector, System.Type, Hl7.Fhir.Introspection.ClassMapping ByRef)
       test      eax,eax
       je        short M70_L02
       mov       rax,[rsp+30]
       jmp       short M70_L00
M70_L02:
       mov       ecx,10C34
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rsi,rax
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       rbx,rax
       mov       ecx,1CAC
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rbx,rax
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
M70_L03:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
; Total bytes of code 255
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,98
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rsp+40],ymm4
       vmovdqu   ymmword ptr [rsp+60],ymm4
       vmovdqa   xmmword ptr [rsp+80],xmm4
       xor       eax,eax
       mov       [rsp+90],rax
       mov       rbx,rcx
       mov       rcx,[rbx+18]
       mov       rdx,offset MT_Hl7.Fhir.Serialization.FhirJsonNode
       cmp       [rcx],rdx
       jne       near ptr M71_L12
       mov       rdx,[rcx+10]
       test      rdx,rdx
       je        near ptr M71_L06
       mov       rdx,[rdx+28]
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M71_L06
       mov       rdx,offset MT_System.String
       cmp       [rsi],rdx
       je        short M71_L03
       mov       rdx,rsi
       mov       rcx,7FF8B02460F0
       call      qword ptr [7FF8B017E0B8]; Hl7.Fhir.Serialization.PrimitiveTypeConverter.ConvertTo[[System.__Canon, System.Private.CoreLib]](System.Object)
       mov       rdi,rax
M71_L00:
       test      rdi,rdi
       je        near ptr M71_L07
       mov       rcx,[rbx+10]
       test      rcx,rcx
       je        near ptr M71_L30
       call      qword ptr [7FF8B017DFE0]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.tryMapFhirPrimitiveTypeToSystemType(System.String)
       mov       rbp,rax
       test      rbp,rbp
       je        near ptr M71_L13
M71_L01:
       test      rbp,rbp
       je        near ptr M71_L29
       lea       r8,[rsp+90]
       mov       rcx,rdi
       mov       rdx,rbp
       call      qword ptr [7FF8B017E028]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.tryParse(System.String, System.Type, System.Object ByRef)
       test      eax,eax
       je        near ptr M71_L20
       mov       rax,[rsp+90]
M71_L02:
       add       rsp,98
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M71_L03:
       mov       rax,[rcx+8]
       cmp       byte ptr [rax+0B],0
       jne       near ptr M71_L11
       mov       ebp,[rsi+8]
       test      ebp,ebp
       je        short M71_L05
       movzx     ecx,word ptr [rsi+0C]
       cmp       ecx,100
       jae       short M71_L08
       mov       eax,ecx
       mov       rcx,7FF90A792F18
       test      byte ptr [rax+rcx],80
       jne       short M71_L10
M71_L04:
       dec       ebp
       mov       eax,ebp
       movzx     ecx,word ptr [rsi+rax*2+0C]
       cmp       ecx,100
       jae       short M71_L09
       mov       eax,ecx
       mov       rcx,7FF90A792F18
       test      byte ptr [rax+rcx],80
       jne       short M71_L10
M71_L05:
       mov       rdi,rsi
       jmp       near ptr M71_L00
M71_L06:
       xor       edi,edi
       jmp       near ptr M71_L00
M71_L07:
       xor       eax,eax
       add       rsp,98
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M71_L08:
       call      qword ptr [7FF8B0304150]; System.Globalization.CharUnicodeInfo.GetIsWhiteSpace(Char)
       test      eax,eax
       jne       short M71_L10
       jmp       short M71_L04
M71_L09:
       call      qword ptr [7FF8B0304150]; System.Globalization.CharUnicodeInfo.GetIsWhiteSpace(Char)
       test      eax,eax
       je        short M71_L05
M71_L10:
       mov       rcx,rsi
       mov       edx,3
       call      qword ptr [7FF8B05072E8]
       mov       rdi,rax
       jmp       near ptr M71_L00
M71_L11:
       mov       rdi,rsi
       jmp       near ptr M71_L00
M71_L12:
       mov       r11,7FF8AF572DF0
       call      qword ptr [r11]
       mov       rdi,rax
       jmp       near ptr M71_L00
M71_L13:
       mov       rcx,[rbx+10]
       mov       edx,1
       call      qword ptr [7FF8B017DFF8]
       test      eax,eax
       je        near ptr M71_L01
       mov       rcx,[rbx+20]
       mov       rdx,[rbx+10]
       mov       r11,7FF8AF572E08
       call      qword ptr [r11]
       test      rax,rax
       je        near ptr M71_L15
       mov       rcx,rax
       mov       r11,7FF8AF572E10
       call      qword ptr [r11]
       mov       rbp,rax
       mov       rcx,26D3D8035E8
       mov       r8,[rcx]
       test      r8,r8
       jne       short M71_L14
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Specification.IElementDefinitionSummary, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,26D3D8035E0
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,7FF8B0179FC8
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D8035E8
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,rsi
M71_L14:
       lea       r9,[rsp+48]
       mov       rdx,rbp
       mov       rcx,7FF8B06F7A00
       call      qword ptr [7FF8B0274828]; System.Linq.Enumerable.TryGetFirst[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,Boolean>, Boolean ByRef)
       test      rax,rax
       je        short M71_L15
       mov       rcx,rax
       mov       r11,7FF8AF572E18
       call      qword ptr [r11]
       mov       rdx,rax
       lea       r8,[rsp+40]
       mov       rcx,7FF8B06F7AF0
       call      qword ptr [7FF8AF8252F0]; System.Linq.Enumerable.TryGetFirst[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
       test      rax,rax
       jne       short M71_L16
M71_L15:
       xor       ecx,ecx
       jmp       short M71_L17
M71_L16:
       mov       rcx,rax
       call      qword ptr [7FF8B017DD88]; Hl7.Fhir.Specification.TypeSerializationInfoExtensions.GetTypeName(Hl7.Fhir.Specification.ITypeSerializationInfo)
       mov       rcx,rax
M71_L17:
       test      rcx,rcx
       jne       short M71_L18
       xor       ebp,ebp
       jmp       short M71_L19
M71_L18:
       call      qword ptr [7FF8B017DFE0]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.tryMapFhirPrimitiveTypeToSystemType(System.String)
       mov       rbp,rax
M71_L19:
       test      rbp,rbp
       jne       near ptr M71_L01
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       ecx,111FB
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdi,rax
       mov       rbx,[rbx+10]
       mov       ecx,1CAC
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       r8,rax
       mov       rcx,rdi
       mov       rdx,rbx
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rdx,rax
       mov       rcx,rbp
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbp
       call      CORINFO_HELP_THROW
       int       3
M71_L20:
       mov       r8,[rbx+28]
       cmp       byte ptr [r8+0C],0
       je        near ptr M71_L21
       mov       r8,26D00396C80
       cmp       rbp,r8
       jne       near ptr M71_L21
       lea       r8,[rsp+88]
       mov       rcx,rdi
       mov       rdx,26D00396CA8
       call      qword ptr [7FF8B017E028]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.tryParse(System.String, System.Type, System.Object ByRef)
       test      eax,eax
       je        short M71_L21
       mov       rdx,[rsp+88]
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.DateTime
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8B017E058]
       cmp       [rax],al
       xor       edx,edx
       mov       [rsp+20],edx
       mov       [rsp+28],edx
       mov       [rsp+30],rdx
       lea       rdx,[rsp+50]
       mov       rcx,rax
       xor       r8d,r8d
       xor       r9d,r9d
       call      qword ptr [7FF8B050ED48]
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.Date
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       lea       rdx,[rsp+50]
       mov       rcx,rdi
       mov       r8d,2
       xor       r9d,r9d
       call      qword ptr [7FF8B0305638]; Hl7.Fhir.ElementModel.Types.Date..ctor(System.DateTimeOffset, Hl7.Fhir.ElementModel.Types.DateTimePrecision, Boolean)
       mov       rax,rdi
       jmp       near ptr M71_L02
M71_L21:
       lea       rcx,[rsp+60]
       mov       edx,22
       mov       r8d,2
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rsp+70]
       cmp       ecx,[rsp+80]
       ja        near ptr M71_L28
       mov       rdx,[rsp+78]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+80]
       sub       eax,ecx
       cmp       eax,9
       jb        short M71_L22
       mov       rcx,26D0039D6CC
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+2]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+2],xmm1
       mov       ecx,[rsp+70]
       add       ecx,9
       mov       [rsp+70],ecx
       jmp       short M71_L23
M71_L22:
       lea       rcx,[rsp+60]
       mov       rdx,26D0039D6C0
       call      qword ptr [7FF8B039CB58]
M71_L23:
       lea       rcx,[rsp+60]
       mov       rdx,rdi
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rsp+70]
       cmp       ecx,[rsp+80]
       ja        near ptr M71_L28
       mov       rdx,[rsp+78]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+80]
       sub       eax,ecx
       cmp       eax,18
       jb        short M71_L24
       mov       rcx,26D0039D6F4
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+20]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   xmmword ptr [rdx+20],xmm1
       mov       ecx,[rsp+70]
       add       ecx,18
       mov       [rsp+70],ecx
       jmp       short M71_L25
M71_L24:
       lea       rcx,[rsp+60]
       mov       rdx,26D0039D6E8
       call      qword ptr [7FF8B039CB58]
M71_L25:
       mov       rdx,[rbx+10]
       lea       rcx,[rsp+60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rsp+70]
       cmp       ecx,[rsp+80]
       ja        near ptr M71_L28
       mov       rdx,[rsp+78]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+80]
       sub       eax,ecx
       je        short M71_L26
       mov       rcx,26D002D0C8C
       movzx     eax,word ptr [rcx]
       mov       [rdx],ax
       mov       ecx,[rsp+70]
       inc       ecx
       mov       [rsp+70],ecx
       jmp       short M71_L27
M71_L26:
       lea       rcx,[rsp+60]
       mov       rdx,26D002D0C80
       call      qword ptr [7FF8B039CB58]
M71_L27:
       lea       rcx,[rsp+60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rsi,rax
       mov       rbp,[rbx+18]
       mov       rcx,[rbx+18]
       mov       r11,7FF8AF572DF8
       call      qword ptr [r11]
       mov       rdx,rsi
       mov       [rsp+20],rax
       mov       r8,rbp
       mov       rcx,rbx
       xor       r9d,r9d
       call      qword ptr [7FF8B017E010]
       jmp       short M71_L30
M71_L28:
       call      qword ptr [7FF8AF827798]
       int       3
M71_L29:
       mov       rdx,[rbx+10]
       mov       rcx,26D0039D620
       mov       r8,26D0039D650
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       rdi,rax
       mov       rsi,[rbx+18]
       mov       rcx,[rbx+18]
       mov       r11,7FF8AF572E00
       call      qword ptr [r11]
       mov       rdx,rdi
       mov       [rsp+20],rax
       mov       r8,rsi
       mov       rcx,rbx
       xor       r9d,r9d
       call      qword ptr [7FF8B017E010]
       jmp       near ptr M71_L07
M71_L30:
       mov       rax,rdi
       jmp       near ptr M71_L02
; Total bytes of code 1491
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.buildNewInstance(Hl7.Fhir.Introspection.ClassMapping, Boolean)
       push      rsi
       push      rbx
       sub       rsp,48
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rsp+20],ymm4
       xor       eax,eax
       mov       [rsp+40],rax
       mov       rbx,rcx
       test      dl,dl
       je        short M72_L00
       mov       rdx,[rbx+20]
       mov       rcx,26D002DDB60
       call      qword ptr [7FF8AF56A4C8]; System.RuntimeType.IsAssignableFrom(System.Type)
       test      eax,eax
       je        short M72_L02
M72_L00:
       mov       rcx,[rbx+20]
       mov       rax,offset MT_System.RuntimeType
       cmp       [rcx],rax
       jne       short M72_L03
       call      System.RuntimeTypeHandle.GetAttributes(System.RuntimeType)
M72_L01:
       test      al,80
       jne       near ptr M72_L14
       mov       rcx,rbx
       call      qword ptr [7FF8B017E298]; Hl7.Fhir.Introspection.ClassMapping.CreateInstance()
       test      rax,rax
       je        short M72_L04
       add       rsp,48
       pop       rbx
       pop       rsi
       ret
M72_L02:
       mov       rcx,offset MT_Hl7.Fhir.Model.DynamicPrimitive
       call      CORINFO_HELP_NEWSFAST
       nop
       add       rsp,48
       pop       rbx
       pop       rsi
       ret
M72_L03:
       mov       rax,[rcx]
       mov       rax,[rax+70]
       call      qword ptr [rax+18]
       jmp       short M72_L01
M72_L04:
       lea       rcx,[rsp+20]
       mov       edx,61
       mov       r8d,1
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rsp+30]
       cmp       ecx,[rsp+40]
       ja        near ptr M72_L13
       mov       rdx,[rsp+38]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+40]
       sub       eax,ecx
       cmp       eax,13
       jb        short M72_L05
       mov       rcx,26D003A92DC
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+16]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   xmmword ptr [rdx+16],xmm1
       mov       ecx,[rsp+30]
       add       ecx,13
       mov       [rsp+30],ecx
       jmp       short M72_L06
M72_L05:
       lea       rcx,[rsp+20]
       mov       rdx,26D003A92D0
       call      qword ptr [7FF8B039CB58]
M72_L06:
       mov       rdx,[rbx+18]
       lea       rcx,[rsp+20]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rsp+30]
       cmp       ecx,[rsp+40]
       ja        near ptr M72_L13
       mov       rdx,[rsp+38]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+40]
       sub       eax,ecx
       cmp       eax,13
       jb        short M72_L07
       mov       rcx,26D003A931C
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+16]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   xmmword ptr [rdx+16],xmm1
       mov       ecx,[rsp+30]
       add       ecx,13
       mov       [rsp+30],ecx
       jmp       short M72_L08
M72_L07:
       lea       rcx,[rsp+20]
       mov       rdx,26D003A9310
       call      qword ptr [7FF8B039CB58]
M72_L08:
       mov       ecx,[rsp+30]
       cmp       ecx,[rsp+40]
       ja        near ptr M72_L13
       mov       rdx,[rsp+38]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+40]
       sub       eax,ecx
       cmp       eax,1C
       jb        short M72_L09
       mov       rcx,26D003A935C
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+18]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+18],ymm1
       mov       ecx,[rsp+30]
       add       ecx,1C
       mov       [rsp+30],ecx
       jmp       short M72_L10
M72_L09:
       lea       rcx,[rsp+20]
       mov       rdx,26D003A9350
       call      qword ptr [7FF8B039CB58]
M72_L10:
       mov       ecx,[rsp+30]
       cmp       ecx,[rsp+40]
       ja        near ptr M72_L13
       mov       rdx,[rsp+38]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rsp+40]
       sub       eax,ecx
       cmp       eax,1F
       jb        short M72_L11
       mov       rcx,26D003A93AC
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+1E]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+1E],ymm1
       mov       ecx,[rsp+30]
       add       ecx,1F
       mov       [rsp+30],ecx
       jmp       short M72_L12
M72_L11:
       lea       rcx,[rsp+20]
       mov       rdx,26D003A93A0
       call      qword ptr [7FF8B039CB58]
M72_L12:
       lea       rcx,[rsp+20]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rbx,rax
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rsi
       call      CORINFO_HELP_THROW
       int       3
M72_L13:
       call      qword ptr [7FF8AF827798]
       int       3
M72_L14:
       mov       rcx,rbx
       call      qword ptr [7FF8B0084000]; Hl7.Fhir.Introspection.ClassMapping.get_IsResource()
       test      eax,eax
       jne       short M72_L15
       mov       rcx,offset MT_Hl7.Fhir.Model.DynamicDataType
       call      CORINFO_HELP_NEWSFAST
       nop
       add       rsp,48
       pop       rbx
       pop       rsi
       ret
M72_L15:
       mov       rcx,offset MT_Hl7.Fhir.Model.DynamicResource
       call      CORINFO_HELP_NEWSFAST
       nop
       add       rsp,48
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 690
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.convertTypedElementValue(System.Object)
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       r8,rbx
       test      r8,r8
       je        short M73_L00
       mov       rax,offset MT_System.String
       cmp       [r8],rax
       jne       short M73_L05
       xor       r8d,r8d
M73_L00:
       test      r8,r8
       jne       near ptr M73_L11
       mov       r10,rbx
       test      r10,r10
       je        short M73_L01
       mov       rax,offset MT_System.String
       cmp       [r10],rax
       jne       short M73_L06
       xor       r10d,r10d
M73_L01:
       test      r10,r10
       jne       near ptr M73_L14
       mov       r9,rbx
       test      r9,r9
       je        short M73_L02
       mov       rax,offset MT_System.String
       cmp       [r9],rax
       jne       short M73_L07
       xor       r9d,r9d
M73_L02:
       test      r9,r9
       jne       near ptr M73_L09
       test      rbx,rbx
       je        short M73_L03
       mov       rax,offset MT_System.Int64
       cmp       [rbx],rax
       je        short M73_L08
M73_L03:
       mov       rax,rbx
M73_L04:
       add       rsp,20
       pop       rbx
       ret
M73_L05:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.DateTime
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r8,rax
       jmp       near ptr M73_L00
M73_L06:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.Time
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r10,rax
       jmp       short M73_L01
M73_L07:
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.Date
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r9,rax
       jmp       short M73_L02
M73_L08:
       mov       rcx,[rbx+8]
       mov       r8,26D3D800180
       mov       r8,[r8]
       xor       edx,edx
       call      qword ptr [7FF8B050EE50]
       jmp       short M73_L04
M73_L09:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.Date
       cmp       [rbx],rcx
       jne       short M73_L13
       mov       rcx,rbx
       call      qword ptr [7FF8B0001DB8]; Hl7.Fhir.ElementModel.Types.Date.ToString()
M73_L10:
       jmp       near ptr M73_L04
M73_L11:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.Types.DateTime
       cmp       [rbx],rcx
       jne       short M73_L15
       mov       rcx,rbx
       call      qword ptr [7FF8B0002588]; Hl7.Fhir.ElementModel.Types.DateTime.ToString()
M73_L12:
       jmp       near ptr M73_L04
M73_L13:
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+8]
       jmp       short M73_L10
M73_L14:
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+8]
       jmp       near ptr M73_L04
M73_L15:
       mov       rcx,rbx
       mov       rax,[rbx]
       mov       rax,[rax+40]
       call      qword ptr [rax+8]
       jmp       short M73_L12
; Total bytes of code 347
```
```assembly
; Hl7.Fhir.Introspection.PropertyMapping.buildTypes()
       push      rbp
       sub       rsp,0A0
       lea       rbp,[rsp+0A0]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-80],ymm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       vmovdqu   ymmword ptr [rbp-40],ymm4
       vmovdqu   ymmword ptr [rbp-20],ymm4
       mov       [rbp+10],rcx
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8B0276220]; Hl7.Fhir.Introspection.PropertyMapping.get_PropertyTypeMapping()
       mov       rcx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFEAFF78]; Hl7.Fhir.Introspection.ClassMapping.get_IsBackboneType()
       test      eax,eax
       je        short M74_L00
       mov       rcx,offset MT_Hl7.Fhir.Specification.ITypeSerializationInfo[]
       mov       edx,1
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       [rbp-48],rax
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8B0276220]; Hl7.Fhir.Introspection.PropertyMapping.get_PropertyTypeMapping()
       mov       [rbp-50],rax
       mov       r8,[rbp-50]
       mov       rcx,[rbp-48]
       xor       edx,edx
       call      qword ptr [7FF8AF615758]; System.Runtime.CompilerServices.CastHelpers.StelemRef(System.Object[], IntPtr, System.Object)
       mov       rax,[rbp-48]
       add       rsp,0A0
       pop       rbp
       ret
M74_L00:
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8B0276238]; Hl7.Fhir.Introspection.PropertyMapping.get_IsPrimitive()
       test      eax,eax
       je        near ptr M74_L01
       mov       rcx,offset MT_System.NotSupportedException
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-40],rax
       mov       ecx,0C69D
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-58],rax
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8B017DD70]; Hl7.Fhir.Introspection.PropertyMapping.get_Name()
       mov       [rbp-60],rax
       mov       ecx,0C6EB
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       [rbp-68],rax
       mov       rcx,[rbp-58]
       mov       rdx,[rbp-60]
       mov       r8,[rbp-68]
       call      qword ptr [7FF8AF82E2E0]; System.String.Concat(System.String, System.String, System.String)
       mov       [rbp-70],rax
       mov       rdx,[rbp-70]
       mov       rcx,[rbp-40]
       call      qword ptr [7FF8AF61F048]
       mov       rcx,[rbp-40]
       call      CORINFO_HELP_THROW
       int       3
M74_L01:
       mov       rcx,offset MT_System.Func<System.Type, System.String>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-8],rax
       mov       rcx,[rbp+10]
       call      qword ptr [7FF8B0276250]; Hl7.Fhir.Introspection.PropertyMapping.get_FhirType()
       mov       [rbp-10],rax
       mov       rcx,[rbp-8]
       mov       rdx,[rbp+10]
       mov       r8,offset Hl7.Fhir.Introspection.PropertyMapping.<buildTypes>g__getFhirTypeName|112_1(System.Type)
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       r8,[rbp-8]
       mov       rdx,[rbp-10]
       mov       rcx,7FF8AFEC7040
       call      qword ptr [7FF8AF967738]; System.Linq.Enumerable.Select[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)
       mov       [rbp-20],rax
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping+<>c
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,26D3D8037D8
       mov       rax,[rax]
       mov       [rbp-18],rax
       mov       rax,[rbp-20]
       mov       [rbp-28],rax
       mov       rax,[rbp-18]
       mov       [rbp-30],rax
       cmp       qword ptr [rbp-18],0
       jne       short M74_L02
       mov       rcx,offset MT_System.Func<System.String, Hl7.Fhir.Specification.ITypeSerializationInfo>
       call      CORINFO_HELP_NEWSFAST
       mov       [rbp-38],rax
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping+<>c
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,26D3D8037D0
       mov       rax,[rax]
       mov       [rbp-78],rax
       mov       rdx,[rbp-78]
       mov       rcx,[rbp-38]
       mov       r8,offset Hl7.Fhir.Introspection.PropertyMapping+<>c.<buildTypes>b__112_0(System.String)
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping+<>c
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rdx,[rbp-38]
       mov       rcx,26D3D8037D8
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,[rbp-38]
       mov       [rbp-30],rax
M74_L02:
       mov       r8,[rbp-30]
       mov       rdx,[rbp-28]
       mov       rcx,7FF8B02AACD8
       call      qword ptr [7FF8AF967738]; System.Linq.Enumerable.Select[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)
       mov       [rbp-80],rax
       mov       rdx,[rbp-80]
       mov       rcx,7FF8B02AAD60
       call      qword ptr [7FF8AF967858]; System.Linq.Enumerable.ToArray[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       nop
       add       rsp,0A0
       pop       rbp
       ret
; Total bytes of code 596
```
```assembly
; System.PackedSpanHelpers.Contains(Int16 ByRef, Int16, Int32)
       cmp       r8d,8
       jge       short M75_L03
       xor       eax,eax
       cmp       r8d,4
       jl        short M75_L00
       add       r8d,0FFFFFFFC
       movsx     rax,word ptr [rcx]
       movsx     r10,dx
       cmp       eax,r10d
       je        near ptr M75_L07
       movsx     rax,word ptr [rcx+2]
       cmp       eax,r10d
       je        near ptr M75_L07
       movsx     rax,word ptr [rcx+4]
       cmp       eax,r10d
       je        near ptr M75_L07
       movsx     rax,word ptr [rcx+6]
       cmp       eax,r10d
       je        near ptr M75_L07
       mov       eax,4
M75_L00:
       test      r8d,r8d
       jle       short M75_L02
       movsx     r10,dx
       add       rax,rax
M75_L01:
       dec       r8d
       movsx     rdx,word ptr [rcx+rax]
       cmp       edx,r10d
       je        near ptr M75_L07
       add       rax,2
       test      r8d,r8d
       jg        short M75_L01
M75_L02:
       xor       eax,eax
       vzeroupper
       ret
M75_L03:
       mov       rax,rcx
       cmp       r8d,10
       jg        short M75_L04
       movzx     edx,dl
       vmovd     xmm0,edx
       vpbroadcastb xmm0,xmm0
       add       r8d,0FFFFFFF8
       movsxd    rax,r8d
       lea       rax,[rcx+rax*2]
       cmp       rcx,rax
       cmova     rcx,rax
       vmovups   xmm1,[rcx]
       vpackuswb xmm1,xmm1,[rax]
       vpcmpeqb  xmm0,xmm1,xmm0
       vptest    xmm0,xmm0
       je        short M75_L02
       jmp       short M75_L07
M75_L04:
       movzx     edx,dl
       vmovd     xmm0,edx
       vpbroadcastb ymm0,xmm0
       cmp       r8d,20
       jle       short M75_L06
       lea       edx,[r8-20]
       movsxd    rdx,edx
       lea       rdx,[rax+rdx*2]
M75_L05:
       vmovups   ymm1,[rax]
       vpackuswb ymm1,ymm1,[rax+20]
       vpcmpeqb  ymm1,ymm1,ymm0
       vptest    ymm1,ymm1
       jne       short M75_L07
       add       rax,40
       cmp       rax,rdx
       jb        short M75_L05
M75_L06:
       lea       edx,[r8-10]
       movsxd    rdx,edx
       lea       rcx,[rcx+rdx*2]
       cmp       rax,rcx
       cmova     rax,rcx
       vmovups   ymm1,[rax]
       vpackuswb ymm1,ymm1,[rcx]
       vpcmpeqb  ymm0,ymm1,ymm0
       vptest    ymm0,ymm0
       je        near ptr M75_L02
M75_L07:
       mov       eax,1
       vzeroupper
       ret
; Total bytes of code 294
```
```assembly
; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Boolean ByRef, System.Object ByRef, System.Func`1<System.__Canon>)
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+60]
       mov       [rbp-40],rsp
       mov       rbx,rdx
       mov       rsi,r8
       mov       rdi,r9
       mov       r14,[rbp+30]
       mov       r15,[rdi]
       test      r15,r15
       je        short M76_L06
M76_L00:
       mov       [rbp-38],r15
       xor       edx,edx
       mov       [rbp-30],edx
       cmp       byte ptr [rbp-30],0
       jne       short M76_L03
       lea       rdx,[rbp-30]
       mov       rcx,r15
       call      System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
       cmp       byte ptr [rsi],0
       jne       short M76_L04
       mov       rcx,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
       cmp       [r14+18],rcx
       jne       short M76_L02
       mov       rcx,[r14+8]
       call      qword ptr [7FF8B017DEC0]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.valueFactory()
M76_L01:
       mov       rcx,rbx
       mov       rdx,rax
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       byte ptr [rsi],1
       jmp       short M76_L04
M76_L02:
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       jmp       short M76_L01
M76_L03:
       call      qword ptr [7FF8B0506658]
       int       3
M76_L04:
       cmp       byte ptr [rbp-30],0
       je        short M76_L05
       mov       rcx,r15
       call      System.Threading.Monitor.Exit(System.Object)
M76_L05:
       mov       rax,[rbx]
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
M76_L06:
       mov       rcx,offset MT_System.Object
       call      CORINFO_HELP_NEWSFAST
       mov       rdx,rax
       mov       rcx,rdi
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       r15,rax
       test      r15,r15
       jne       near ptr M76_L00
       mov       r15,[rdi]
       jmp       near ptr M76_L00
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+60]
       cmp       byte ptr [rbp-30],0
       je        short M76_L07
       mov       rcx,[rbp-38]
       call      System.Threading.Monitor.Exit(System.Object)
M76_L07:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 266
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.get_Name()
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rcx,[rbx+30]
       test      rcx,rcx
       je        short M77_L01
       mov       rax,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],rax
       jne       short M77_L02
       mov       rax,[rcx+8]
M77_L00:
       test      rax,rax
       je        short M77_L03
       add       rsp,20
       pop       rbx
       ret
M77_L01:
       xor       eax,eax
       jmp       short M77_L00
M77_L02:
       mov       r11,7FF8AF5727E8
       call      qword ptr [r11]
       jmp       short M77_L00
M77_L03:
       mov       rcx,[rbx+18]
       mov       r11,7FF8AF5727F0
       cmp       [rcx],ecx
       add       rsp,20
       pop       rbx
       jmp       qword ptr [r11]
; Total bytes of code 90
```
```assembly
; Hl7.Fhir.Introspection.ClassMapping.Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements()
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,1F8
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rsp+30],xmm4
       mov       rax,0FFFFFFFFFFFFFE50
M78_L00:
       vmovdqa   xmmword ptr [rsp+rax+1F0],xmm4
       vmovdqa   xmmword ptr [rsp+rax+200],xmm4
       vmovdqa   xmmword ptr [rsp+rax+210],xmm4
       add       rax,30
       jne       short M78_L00
       mov       [rsp+1F0],rax
       mov       rbx,rcx
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMappingCollection>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       cmp       [rbx],bl
       lea       rdi,[rbx+40]
       lea       rcx,[rsi+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Hl7.Fhir.Introspection.ClassMapping.<get_PropertyMappingsInternal>g__createCollection|49_0()
       mov       [rsi+18],rcx
       mov       rbx,[rdi]
       test      rbx,rbx
       je        near ptr M78_L13
M78_L01:
       cmp       [rbx],bl
       mov       rcx,offset MT_System.Func<System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       lea       r14,[rbx+10]
       lea       rcx,[rbp+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rdx,offset Hl7.Fhir.Introspection.PropertyMappingCollection.<get_ByOrder>b__17_0()
       mov       [rbp+18],rdx
       mov       rbx,[r14]
       test      rbx,rbx
       je        near ptr M78_L16
M78_L02:
       mov       rdx,26D3D801D50
       mov       r15,[rdx]
       test      r15,r15
       je        near ptr M78_L33
M78_L03:
       test      rbx,rbx
       je        near ptr M78_L43
       test      r15,r15
       je        near ptr M78_L42
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M78_L36
       mov       rdx,rbx
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping[]
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       mov       r13,rax
       test      r13,r13
       jne       near ptr M78_L20
       mov       rdx,rbx
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rsi,rax
       test      rsi,rsi
       je        near ptr M78_L19
       mov       rcx,offset MT_System.Linq.Enumerable+ListWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r12+10],eax
       lea       rcx,[r12+18]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+20]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
M78_L04:
       test      r12,r12
       je        near ptr M78_L43
       mov       rdx,r12
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        near ptr M78_L32
       mov       rcx,offset MT_System.Linq.Enumerable+ListWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       cmp       [rax],rcx
       jne       near ptr M78_L41
       mov       rcx,[rax+18]
       xor       esi,esi
       xor       edi,edi
       test      rcx,rcx
       je        short M78_L05
       mov       edi,[rcx+10]
       mov       rsi,[rcx+8]
       cmp       [rsi+8],edi
       jb        near ptr M78_L39
       add       rsi,10
M78_L05:
       mov       r13,[rax+20]
       vxorps    ymm0,ymm0,ymm0
       vmovdqu   ymmword ptr [rsp+1B8],ymm0
       vmovdqu   ymmword ptr [rsp+1D8],ymm0
       vxorps    ymm0,ymm0,ymm0
       vmovdqu   ymmword ptr [rsp+0C0],ymm0
       vmovdqu   ymmword ptr [rsp+0E0],ymm0
       vmovdqu   ymmword ptr [rsp+100],ymm0
       vmovdqu   ymmword ptr [rsp+120],ymm0
       vmovdqu   ymmword ptr [rsp+140],ymm0
       vmovdqu   ymmword ptr [rsp+160],ymm0
       vmovdqu   ymmword ptr [rsp+178],ymm0
       xor       ecx,ecx
       mov       [rsp+0B0],ecx
       mov       [rsp+0B4],ecx
       mov       [rsp+0B8],ecx
       lea       rcx,[rsp+1B8]
       mov       [rsp+198],rcx
       mov       dword ptr [rsp+1A0],8
       lea       rcx,[rsp+1B8]
       mov       [rsp+1A8],rcx
       mov       dword ptr [rsp+1B0],8
       test      edi,edi
       jle       short M78_L08
       test      r13,r13
       je        near ptr M78_L23
       mov       rcx,offset Hl7.Fhir.Introspection.ClassMapping+<>c.<Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements>b__66_0(Hl7.Fhir.Introspection.PropertyMapping)
       cmp       [r13+18],rcx
       jne       near ptr M78_L23
       xor       r13d,r13d
M78_L06:
       mov       r12,[rsi+r13]
       cmp       byte ptr [r12+6A],0
       jne       short M78_L07
       mov       rax,[rsp+1A8]
       mov       r8d,[rsp+1B0]
       mov       r10d,[rsp+0B8]
       cmp       r10d,r8d
       jae       near ptr M78_L22
       mov       ecx,r10d
       lea       rcx,[rax+rcx*8]
       mov       rdx,r12
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       ecx,[rsp+0B8]
       inc       ecx
       mov       [rsp+0B8],ecx
M78_L07:
       add       r13,8
       dec       edi
       jne       short M78_L06
M78_L08:
       mov       ebp,[rsp+0B4]
       add       ebp,[rsp+0B8]
       jo        near ptr M78_L46
       test      ebp,ebp
       je        near ptr M78_L28
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       test      ebp,ebp
       jl        near ptr M78_L40
       movsxd    rdx,ebp
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       lea       rcx,[rsi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8d,ebp
       mov       rdx,rsi
       mov       rcx,7FF8B028AF98
       call      qword ptr [7FF8AFEA6B80]; System.Runtime.InteropServices.CollectionsMarshal.SetCount[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.List`1<System.__Canon>, Int32)
       mov       ecx,[rsi+10]
       mov       rdx,[rsi+8]
       cmp       [rdx+8],ecx
       jb        near ptr M78_L39
       add       rdx,10
       mov       [rsp+90],rdx
       mov       [rsp+98],ecx
       mov       ebp,[rsp+0B0]
       test      ebp,ebp
       jne       near ptr M78_L29
M78_L09:
       mov       ecx,[rsp+0B8]
       mov       [rsp+20],ecx
       lea       rcx,[rsp+1A8]
       lea       rdx,[rsp+0A0]
       mov       r8,offset MT_System.Span<Hl7.Fhir.Introspection.PropertyMapping>
       xor       r9d,r9d
       call      qword ptr [7FF8AFEA6D18]; System.Span`1[[System.__Canon, System.Private.CoreLib]].Slice(Int32, Int32)
       vmovdqu   xmm0,xmmword ptr [rsp+90]
       vmovdqu   xmmword ptr [rsp+30],xmm0
       lea       r8,[rsp+30]
       lea       rcx,[rsp+0A0]
       mov       rdx,offset MT_System.Span<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA6D30]; System.Span`1[[System.__Canon, System.Private.CoreLib]].CopyTo(System.Span`1<System.__Canon>)
M78_L10:
       mov       r8d,[rsp+0B0]
       test      r8d,r8d
       jne       near ptr M78_L31
M78_L11:
       mov       rax,rsi
M78_L12:
       add       rsp,1F8
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M78_L13:
       mov       rcx,offset Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       cmp       [rsi+18],rcx
       jne       short M78_L15
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       rcx,r14
       call      qword ptr [7FF8B017EBB0]; Hl7.Fhir.Utility.AnnotationList..ctor()
M78_L14:
       test      r14,r14
       je        near ptr M78_L45
       mov       rcx,rdi
       mov       rdx,r14
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       rbx,[rdi]
       jmp       near ptr M78_L01
M78_L15:
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       mov       r14,rax
       jmp       short M78_L14
M78_L16:
       mov       rcx,offset Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       cmp       [rbp+18],rcx
       jne       short M78_L18
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,r15
       call      qword ptr [7FF8B017EBB0]; Hl7.Fhir.Utility.AnnotationList..ctor()
M78_L17:
       test      r15,r15
       je        near ptr M78_L44
       mov       rcx,r14
       mov       rdx,r15
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       rbx,[r14]
       jmp       near ptr M78_L02
M78_L18:
       mov       rcx,[rbp+8]
       call      qword ptr [rbp+18]
       mov       r15,rax
       jmp       short M78_L17
M78_L19:
       mov       rcx,offset MT_System.Linq.Enumerable+IEnumerableWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r12+10],eax
       lea       rcx,[r12+18]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r12+20]
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M78_L04
M78_L20:
       cmp       dword ptr [r13+8],0
       jne       near ptr M78_L35
       test      byte ptr [7FF8B06F7900],1
       je        near ptr M78_L34
M78_L21:
       mov       rcx,26D3D8047B0
       mov       r12,[rcx]
       jmp       near ptr M78_L04
M78_L22:
       lea       rcx,[rsp+0B0]
       mov       r8,r12
       mov       rdx,offset MT_System.Collections.Generic.SegmentedArrayBuilder<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA69B8]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].AddSlow(System.__Canon)
       jmp       near ptr M78_L07
M78_L23:
       xor       ebp,ebp
       mov       r15d,edi
M78_L24:
       mov       r12,[rsi+rbp]
       mov       rcx,offset Hl7.Fhir.Introspection.ClassMapping+<>c.<Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements>b__66_0(Hl7.Fhir.Introspection.PropertyMapping)
       cmp       [r13+18],rcx
       jne       near ptr M78_L37
       cmp       byte ptr [r12+6A],0
       jne       short M78_L26
M78_L25:
       mov       rax,[rsp+1A8]
       mov       r8d,[rsp+1B0]
       mov       r10d,[rsp+0B8]
       cmp       r10d,r8d
       jae       short M78_L27
       mov       ecx,r10d
       lea       rcx,[rax+rcx*8]
       mov       rdx,r12
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       ecx,[rsp+0B8]
       inc       ecx
       mov       [rsp+0B8],ecx
M78_L26:
       add       rbp,8
       dec       r15d
       jne       short M78_L24
       jmp       near ptr M78_L08
M78_L27:
       lea       rcx,[rsp+0B0]
       mov       r8,r12
       mov       rdx,offset MT_System.Collections.Generic.SegmentedArrayBuilder<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA69B8]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].AddSlow(System.__Canon)
       jmp       short M78_L26
M78_L28:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       call      qword ptr [7FF8AF9675E8]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor()
       jmp       near ptr M78_L10
M78_L29:
       mov       r8,[rsp+198]
       mov       ebx,[rsp+1A0]
       mov       rdx,[rsp+90]
       mov       r9d,[rsp+98]
       cmp       ebx,r9d
       ja        near ptr M78_L38
       mov       r9d,ebx
       mov       rcx,7FF8B06F5E00
       call      qword ptr [7FF8B05063D0]; System.Buffer.Memmove[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.__Canon ByRef, UIntPtr)
       lea       rcx,[rsp+90]
       lea       rdx,[rsp+90]
       mov       r9d,ebx
       mov       r8,offset MT_System.Span<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA6BE0]; System.Span`1[[System.__Canon, System.Private.CoreLib]].Slice(Int32)
       dec       ebp
       je        near ptr M78_L09
       vxorps    xmm0,xmm0,xmm0
       vmovdqu   xmmword ptr [rsp+50],xmm0
       lea       rcx,[rsp+50]
       lea       r8,[rsp+0C0]
       mov       r12,offset MT_System.ReadOnlySpan<Hl7.Fhir.Introspection.PropertyMapping[]>
       mov       rdx,r12
       mov       r9d,1B
       call      qword ptr [7FF8B050ECD0]
       vmovdqu   xmm0,xmmword ptr [rsp+50]
       vmovdqu   xmmword ptr [rsp+80],xmm0
       mov       [rsp+20],ebp
       lea       rcx,[rsp+80]
       lea       rdx,[rsp+70]
       mov       r8,r12
       xor       r9d,r9d
       call      qword ptr [7FF8AFEA6CE8]; System.ReadOnlySpan`1[[System.__Canon, System.Private.CoreLib]].Slice(Int32, Int32)
       xor       r13d,r13d
       mov       r15d,[rsp+78]
       test      r15d,r15d
       jle       near ptr M78_L09
M78_L30:
       cmp       r13d,r15d
       jae       near ptr M78_L47
       mov       rcx,[rsp+70]
       mov       r8,[rcx+r13*8]
       vxorps    xmm0,xmm0,xmm0
       vmovdqu   xmmword ptr [rsp+40],xmm0
       lea       rcx,[rsp+40]
       mov       rbx,offset MT_System.ReadOnlySpan<Hl7.Fhir.Introspection.PropertyMapping>
       mov       rdx,rbx
       call      qword ptr [7FF8B050EB50]
       vmovdqu   xmm0,xmmword ptr [rsp+40]
       vmovdqu   xmmword ptr [rsp+60],xmm0
       vmovdqu   xmm0,xmmword ptr [rsp+90]
       vmovdqu   xmmword ptr [rsp+30],xmm0
       lea       r8,[rsp+30]
       lea       rcx,[rsp+60]
       mov       rdx,rbx
       call      qword ptr [7FF8AFEA6BC8]; System.ReadOnlySpan`1[[System.__Canon, System.Private.CoreLib]].CopyTo(System.Span`1<System.__Canon>)
       lea       rcx,[rsp+90]
       lea       rdx,[rsp+90]
       mov       r9d,[rsp+68]
       mov       r8,offset MT_System.Span<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA6BE0]; System.Span`1[[System.__Canon, System.Private.CoreLib]].Slice(Int32)
       inc       r13d
       cmp       r13d,r15d
       jl        near ptr M78_L30
       jmp       near ptr M78_L09
M78_L31:
       lea       rcx,[rsp+0B0]
       mov       rdx,offset MT_System.Collections.Generic.SegmentedArrayBuilder<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8AFEA6D48]; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].ReturnArrays(Int32)
       jmp       near ptr M78_L11
M78_L32:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       mov       rdx,r12
       call      qword ptr [7FF8AFEA4C00]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor(System.Collections.Generic.IEnumerable`1<System.__Canon>)
       mov       rax,rbx
       jmp       near ptr M78_L12
M78_L33:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMapping, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rdx,26D3D801D30
       mov       rdx,[rdx]
       mov       rcx,r15
       mov       r8,offset Hl7.Fhir.Introspection.ClassMapping+<>c.<Hl7.Fhir.Specification.IStructureDefinitionSummary.GetElements>b__66_0(Hl7.Fhir.Introspection.PropertyMapping)
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D801D50
       mov       rdx,r15
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M78_L03
M78_L34:
       mov       rcx,offset MT_System.Array+EmptyArray<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       jmp       near ptr M78_L21
M78_L35:
       mov       rcx,offset MT_System.Linq.Enumerable+ArrayWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       r12,rax
       mov       rcx,r12
       mov       rdx,r13
       mov       r8,r15
       call      qword ptr [7FF8B050EA48]
       jmp       near ptr M78_L04
M78_L36:
       mov       rcx,rax
       mov       rdx,r15
       mov       rax,[rax]
       mov       rax,[rax+50]
       call      qword ptr [rax+8]
       mov       r12,rax
       jmp       near ptr M78_L04
M78_L37:
       mov       rdx,r12
       mov       rcx,[r13+8]
       call      qword ptr [r13+18]
       test      eax,eax
       je        near ptr M78_L26
       jmp       near ptr M78_L25
M78_L38:
       call      qword ptr [7FF8AFAB5818]
       int       3
M78_L39:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M78_L40:
       mov       ecx,16
       mov       edx,0D
       call      qword ptr [7FF8AFB84A20]
       int       3
M78_L41:
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+28]
       jmp       near ptr M78_L12
M78_L42:
       mov       ecx,0C
       call      qword ptr [7FF8AF61F738]
       int       3
M78_L43:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M78_L44:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B05064D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M78_L45:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B05064D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M78_L46:
       call      CORINFO_HELP_OVERFLOW
       int       3
M78_L47:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 2313
```
```assembly
; System.Linq.Enumerable.ToDictionary[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,System.__Canon>, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       lea       rbp,[rsp+0A0]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-70],ymm4
       vmovdqa   xmmword ptr [rbp-50],xmm4
       mov       [rbp-80],rsp
       mov       [rbp-40],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       r14,r9
       test      rsi,rsi
       je        near ptr M79_L47
       test      rdi,rdi
       je        near ptr M79_L46
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],48
       jle       near ptr M79_L09
       mov       r15,[rcx+48]
       test      r15,r15
       je        near ptr M79_L09
M79_L00:
       mov       rcx,[r15+18]
       mov       rcx,[rcx+20]
       test      rcx,rcx
       je        near ptr M79_L10
M79_L01:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       r13,rax
       test      r13,r13
       je        near ptr M79_L11
       mov       rcx,[r15+18]
       cmp       qword ptr [rcx+8],30
       jle       near ptr M79_L16
       mov       r11,[rcx+30]
       test      r11,r11
       je        near ptr M79_L16
M79_L02:
       mov       rcx,r13
       call      qword ptr [r11]
       mov       r12d,eax
M79_L03:
       test      r12d,r12d
       je        near ptr M79_L43
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],68
       jle       near ptr M79_L17
       mov       rcx,[rcx+68]
       test      rcx,rcx
       je        near ptr M79_L17
M79_L04:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       test      rax,rax
       jne       near ptr M79_L40
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],70
       jle       near ptr M79_L18
       mov       rcx,[rcx+70]
       test      rcx,rcx
       je        near ptr M79_L18
M79_L05:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       jne       near ptr M79_L34
M79_L06:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],50
       jle       near ptr M79_L19
       mov       rcx,[rcx+50]
       test      rcx,rcx
       je        near ptr M79_L19
M79_L07:
       call      CORINFO_HELP_NEWSFAST
       mov       r13,rax
       mov       rcx,r13
       mov       edx,r12d
       mov       r8,r14
       call      qword ptr [7FF8AF615908]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],58
       jle       near ptr M79_L20
       mov       r11,[rcx+58]
       test      r11,r11
       je        near ptr M79_L20
M79_L08:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       [rbp-78],rax
       jmp       near ptr M79_L21
M79_L09:
       mov       rcx,rbx
       mov       rdx,7FF8B0596320
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r15,rax
       jmp       near ptr M79_L00
M79_L10:
       mov       rcx,r15
       mov       rdx,7FF8B0596580
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M79_L01
M79_L11:
       mov       rcx,[r15+18]
       mov       rcx,[rcx+28]
       test      rcx,rcx
       je        short M79_L13
M79_L12:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M79_L14
       mov       rcx,rax
       mov       edx,1
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       r12d,eax
       test      r12d,r12d
       jl        short M79_L14
       jmp       near ptr M79_L03
M79_L13:
       mov       rcx,r15
       mov       rdx,7FF8B0596590
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M79_L12
M79_L14:
       mov       rdx,rsi
       mov       rcx,offset MT_System.Collections.ICollection
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       jne       short M79_L15
       xor       r12d,r12d
       jmp       near ptr M79_L06
M79_L15:
       mov       rcx,rax
       mov       r11,7FF8AF572CD0
       call      qword ptr [r11]
       mov       r12d,eax
       jmp       near ptr M79_L03
M79_L16:
       mov       rcx,r15
       mov       rdx,7FF8B0596628
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       near ptr M79_L02
M79_L17:
       mov       rcx,rbx
       mov       rdx,7FF8B0596490
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M79_L04
M79_L18:
       mov       rcx,rbx
       mov       rdx,7FF8B0596498
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M79_L05
M79_L19:
       mov       rcx,rbx
       mov       rdx,7FF8B0596340
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       near ptr M79_L07
M79_L20:
       mov       rcx,rbx
       mov       rdx,7FF8B0596460
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       near ptr M79_L08
M79_L21:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>+Enumerator
       cmp       [rax],rcx
       jne       near ptr M79_L28
       lea       r14,[rax+8]
       mov       rcx,[r14]
       mov       edx,[r14+14]
       cmp       edx,[rcx+14]
       jne       near ptr M79_L27
       mov       edx,[r14+10]
       cmp       edx,[rcx+10]
       jae       near ptr M79_L25
       mov       rcx,[rcx+8]
       cmp       edx,[rcx+8]
       jae       near ptr M79_L31
       mov       edx,edx
       mov       rdx,[rcx+rdx*8+10]
       lea       rcx,[r14+8]
       call      CORINFO_HELP_ASSIGN_REF
       inc       dword ptr [r14+10]
M79_L22:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],60
       jle       short M79_L26
       mov       r11,[rcx+60]
       test      r11,r11
       je        short M79_L26
M79_L23:
       mov       rcx,[rbp-78]
       call      qword ptr [r11]
       mov       rsi,rax
       mov       rcx,offset Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<>c.<Children>b__36_0(Hl7.Fhir.Specification.IElementDefinitionSummary)
       cmp       [rdi+18],rcx
       jne       near ptr M79_L30
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rsi],rcx
       jne       short M79_L29
       mov       rdx,[rsi+8]
M79_L24:
       mov       rcx,r13
       mov       r8,rsi
       mov       r9d,2
       call      qword ptr [7FF8AF6164F0]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryInsert(System.__Canon, System.__Canon, System.Collections.Generic.InsertionBehavior)
       mov       rax,[rbp-78]
       jmp       near ptr M79_L21
M79_L25:
       mov       rax,[rbp-78]
       mov       rcx,[r14]
       mov       ecx,[rcx+10]
       inc       ecx
       mov       [r14+10],ecx
       xor       ecx,ecx
       mov       [r14+8],rcx
       jmp       short M79_L32
M79_L26:
       mov       rcx,rbx
       mov       rdx,7FF8B0596478
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M79_L23
M79_L27:
       call      qword ptr [7FF8AF61FA38]
       int       3
M79_L28:
       mov       rcx,rax
       mov       r11,7FF8AF572CC0
       call      qword ptr [r11]
       test      eax,eax
       jne       near ptr M79_L22
       jmp       short M79_L33
M79_L29:
       mov       rcx,rsi
       mov       r11,7FF8AF572CD8
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       short M79_L24
M79_L30:
       mov       rdx,rsi
       mov       rcx,[rdi+8]
       call      qword ptr [rdi+18]
       mov       rdx,rax
       jmp       near ptr M79_L24
M79_L31:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
M79_L32:
       mov       rax,r13
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M79_L33:
       mov       rcx,[rbp-78]
       mov       r11,7FF8AF572CC8
       call      qword ptr [r11]
       jmp       short M79_L32
M79_L34:
       mov       esi,[rax+10]
       mov       r15,[rax+8]
       cmp       [r15+8],esi
       jb        near ptr M79_L39
       add       r15,10
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],80
       jle       short M79_L35
       mov       r13,[rcx+80]
       test      r13,r13
       je        short M79_L35
       jmp       short M79_L36
M79_L35:
       mov       rcx,rbx
       mov       rdx,7FF8B05964C8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r13,rax
M79_L36:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],88
       jle       short M79_L37
       mov       r12,[rcx+88]
       test      r12,r12
       je        short M79_L37
       jmp       short M79_L38
M79_L37:
       mov       rcx,rbx
       mov       rdx,7FF8B0596550
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r12,rax
M79_L38:
       mov       [rbp-60],r15
       mov       [rbp-58],esi
       lea       r8,[rbp-60]
       lea       rcx,[rbp-50]
       mov       rdx,r13
       call      qword ptr [7FF8AFEA4D08]; System.Span`1[[System.__Canon, System.Private.CoreLib]].op_Implicit(System.Span`1<System.__Canon>)
       lea       rdx,[rbp-50]
       mov       rcx,r12
       mov       r8,rdi
       mov       r9,r14
       call      qword ptr [7FF8B0506748]
       nop
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M79_L39:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M79_L40:
       lea       rsi,[rax+10]
       mov       r15d,[rax+8]
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],88
       jle       short M79_L41
       mov       rcx,[rcx+88]
       test      rcx,rcx
       je        short M79_L41
       jmp       short M79_L42
M79_L41:
       mov       rcx,rbx
       mov       rdx,7FF8B0596550
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
M79_L42:
       mov       [rbp-70],rsi
       mov       [rbp-68],r15d
       lea       rdx,[rbp-70]
       mov       r8,rdi
       mov       r9,r14
       call      qword ptr [7FF8B0506748]
       nop
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M79_L43:
       mov       rcx,[rbx+18]
       cmp       qword ptr [rcx+10],50
       jle       short M79_L44
       mov       rcx,[rcx+50]
       test      rcx,rcx
       je        short M79_L44
       jmp       short M79_L45
M79_L44:
       mov       rcx,rbx
       mov       rdx,7FF8B0596340
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
M79_L45:
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       mov       r8,r14
       xor       edx,edx
       call      qword ptr [7FF8AF615908]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]]..ctor(Int32, System.Collections.Generic.IEqualityComparer`1<System.__Canon>)
       mov       rax,rbx
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M79_L46:
       mov       ecx,9
       call      qword ptr [7FF8AF61F738]
       int       3
M79_L47:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+0A0]
       cmp       qword ptr [rbp-78],0
       je        short M79_L48
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>+Enumerator
       mov       r11,[rbp-78]
       cmp       [r11],rcx
       je        short M79_L48
       mov       rcx,r11
       mov       r11,7FF8AF572CC8
       call      qword ptr [r11]
M79_L48:
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 1520
```
```assembly
; System.Linq.Enumerable.Any[[System.Collections.Generic.KeyValuePair`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]], System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.__Canon,System.__Canon>>)
       push      rbp
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,50
       lea       rbp,[rsp+70]
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-38],rax
       mov       [rbp-50],rsp
       mov       [rbp-28],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M80_L15
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+10]
       test      rcx,rcx
       je        short M80_L03
M80_L00:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rdi,rax
       test      rdi,rdi
       je        short M80_L06
       mov       rcx,[rbx+18]
       mov       r11,[rcx+28]
       test      r11,r11
       je        short M80_L04
M80_L01:
       mov       rcx,rdi
       call      qword ptr [r11]
       test      eax,eax
       setne     dil
       movzx     edi,dil
M80_L02:
       movzx     eax,dil
       add       rsp,50
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
M80_L03:
       mov       rcx,rbx
       mov       rdx,7FF8B025F718
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
       jmp       short M80_L00
M80_L04:
       mov       rcx,rbx
       mov       rdx,7FF8B025F750
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M80_L01
M80_L05:
       mov       rcx,[rbp-48]
       mov       r11,7FF8AF572820
       call      qword ptr [r11]
       mov       edi,eax
       jmp       near ptr M80_L13
M80_L06:
       mov       rcx,[rbx+18]
       mov       rcx,[rcx+18]
       test      rcx,rcx
       je        short M80_L07
       jmp       short M80_L08
M80_L07:
       mov       rcx,rbx
       mov       rdx,7FF8B025F728
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rcx,rax
M80_L08:
       mov       rdx,rsi
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       r14,rax
       test      r14,r14
       je        short M80_L10
       mov       rcx,r14
       mov       edx,1
       mov       rax,[r14]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       test      eax,eax
       jl        short M80_L09
       test      eax,eax
       setne     dil
       movzx     edi,dil
       jmp       near ptr M80_L02
M80_L09:
       lea       r8,[rbp-30]
       lea       rdx,[rbp-40]
       mov       rcx,r14
       mov       rax,[r14]
       mov       rax,[rax+48]
       call      qword ptr [rax+10]
       movzx     edi,byte ptr [rbp-30]
       jmp       near ptr M80_L02
M80_L10:
       mov       rdx,rsi
       mov       rcx,offset MT_System.Collections.ICollection
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       jne       short M80_L14
       mov       rcx,[rbx+18]
       mov       r11,[rcx+20]
       test      r11,r11
       je        short M80_L11
       jmp       short M80_L12
M80_L11:
       mov       rcx,rbx
       mov       rdx,7FF8B025F738
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
M80_L12:
       mov       rcx,rsi
       call      qword ptr [r11]
       mov       [rbp-48],rax
       jmp       near ptr M80_L05
M80_L13:
       mov       rcx,[rbp-48]
       mov       r11,7FF8AF572828
       call      qword ptr [r11]
       jmp       near ptr M80_L02
M80_L14:
       mov       rcx,rax
       mov       r11,7FF8AF572830
       call      qword ptr [r11]
       test      eax,eax
       setne     dil
       movzx     edi,dil
       jmp       near ptr M80_L02
M80_L15:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
       push      rbp
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+70]
       cmp       qword ptr [rbp-48],0
       je        short M80_L16
       mov       rcx,[rbp-48]
       mov       r11,7FF8AF572828
       call      qword ptr [r11]
M80_L16:
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       rbp
       ret
; Total bytes of code 509
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<runAdditionalRules>d__37..ctor(Int32)
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       [rbx+40],edx
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [rbx+44],eax
       add       rsp,20
       pop       rbx
       ret
; Total bytes of code 25
```
```assembly
; Hl7.Fhir.Introspection.ClassMapping.FindMappedElementByChoiceName(System.String)
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rsp+20],xmm4
       xor       eax,eax
       mov       [rsp+30],rax
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rcx,offset MT_Hl7.Fhir.Introspection.ClassMapping+<>c__DisplayClass59_0
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       lea       rcx,[rdi+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rsi,[rdi+8]
       test      rsi,rsi
       je        near ptr M82_L21
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMappingCollection>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       cmp       [rbx],bl
       lea       r14,[rbx+40]
       lea       rcx,[rbp+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset Hl7.Fhir.Introspection.ClassMapping.<get_PropertyMappingsInternal>g__createCollection|49_0()
       mov       [rbp+18],rcx
       mov       rax,[r14]
       test      rax,rax
       je        short M82_L01
M82_L00:
       mov       rcx,[rax+8]
       test      rcx,rcx
       jne       short M82_L04
       jmp       near ptr M82_L19
M82_L01:
       mov       rcx,offset Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       cmp       [rbp+18],rcx
       jne       short M82_L03
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,r15
       call      qword ptr [7FF8B017EBB0]; Hl7.Fhir.Utility.AnnotationList..ctor()
M82_L02:
       test      r15,r15
       je        near ptr M82_L20
       mov       rcx,r14
       mov       rdx,r15
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       mov       rax,[r14]
       jmp       short M82_L00
M82_L03:
       mov       rcx,[rbp+8]
       call      qword ptr [rbp+18]
       mov       r15,rax
       jmp       short M82_L02
M82_L04:
       lea       r8,[rsp+30]
       mov       rdx,rsi
       mov       r11,7FF8AF572DD8
       call      qword ptr [r11]
       xor       ecx,ecx
       test      eax,eax
       mov       rax,rcx
       cmovne    rax,[rsp+30]
       mov       [rsp+30],rcx
       test      rax,rax
       je        short M82_L05
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M82_L05:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMappingCollection>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       lea       rbp,[rbx+40]
       lea       rcx,[rsi+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,offset Hl7.Fhir.Introspection.ClassMapping.<get_PropertyMappingsInternal>g__createCollection|49_0()
       mov       [rsi+18],r8
       mov       rbx,[rbp]
       test      rbx,rbx
       je        near ptr M82_L10
M82_L06:
       cmp       [rbx],bl
       mov       rcx,offset MT_System.Func<System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       lea       rbp,[rbx+18]
       lea       rcx,[rsi+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,offset Hl7.Fhir.Introspection.PropertyMappingCollection.<get_ChoiceProperties>b__20_0()
       mov       [rsi+18],r8
       mov       rbx,[rbp]
       test      rbx,rbx
       je        near ptr M82_L11
M82_L07:
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMapping, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       lea       rcx,[rsi+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,7FF8B02724C0
       mov       [rsi+18],r8
       mov       r8,rsi
       mov       rdx,rbx
       mov       rcx,7FF8B0260160
       call      qword ptr [7FF8AF965A10]; System.Linq.Enumerable.Where[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`2<System.__Canon,Boolean>)
       mov       rbx,rax
       test      rbx,rbx
       je        near ptr M82_L18
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        near ptr M82_L12
       mov       rdx,offset MT_System.Linq.Enumerable+ListWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       cmp       [rax],rdx
       jne       near ptr M82_L14
       mov       rdx,[rax+18]
       xor       r8d,r8d
       xor       ecx,ecx
       test      rdx,rdx
       je        short M82_L08
       mov       ecx,[rdx+10]
       mov       r8,[rdx+8]
       cmp       [r8+8],ecx
       jb        near ptr M82_L13
       add       r8,10
M82_L08:
       mov       [rsp+20],r8
       mov       [rsp+28],ecx
       lea       rdx,[rsp+20]
       mov       r8,[rax+20]
       mov       rcx,offset MT_System.Linq.Enumerable+ArrayWhereIterator<Hl7.Fhir.Introspection.PropertyMapping>
       call      qword ptr [7FF8B0274F30]; System.Linq.Enumerable+ArrayWhereIterator`1[[System.__Canon, System.Private.CoreLib]].ToList(System.ReadOnlySpan`1<System.__Canon>, System.Func`2<System.__Canon,Boolean>)
       mov       rsi,rax
M82_L09:
       mov       rdx,rsi
       mov       rcx,7FF8B02AC168
       call      qword ptr [7FF8B0175FF8]; System.Linq.Enumerable.Any[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>)
       test      eax,eax
       jne       near ptr M82_L15
       xor       eax,eax
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M82_L10:
       mov       r8,rsi
       mov       rdx,rbp
       mov       rcx,7FF8B0260418
       call      qword ptr [7FF8B017E310]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       mov       rbx,rax
       jmp       near ptr M82_L06
M82_L11:
       mov       r8,rsi
       mov       rdx,rbp
       mov       rcx,7FF8B0287FD8
       call      qword ptr [7FF8B017E310]; System.Threading.LazyInitializer.EnsureInitializedCore[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, System.Func`1<System.__Canon>)
       mov       rbx,rax
       jmp       near ptr M82_L07
M82_L12:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rcx,rsi
       mov       rdx,rbx
       call      qword ptr [7FF8AFEA4C00]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor(System.Collections.Generic.IEnumerable`1<System.__Canon>)
       jmp       near ptr M82_L09
M82_L13:
       call      qword ptr [7FF8AF61F2A0]
       int       3
M82_L14:
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+28]
       mov       rsi,rax
       jmp       near ptr M82_L09
M82_L15:
       cmp       dword ptr [rsi+10],1
       je        short M82_L17
       mov       rcx,26D3D801D48
       mov       r8,[rcx]
       test      r8,r8
       jne       short M82_L16
       mov       rcx,offset MT_System.Func<Hl7.Fhir.Introspection.PropertyMapping, Hl7.Fhir.Introspection.PropertyMapping, Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rdx,26D3D801D30
       mov       rdx,[rdx]
       mov       rcx,rbx
       mov       r8,7FF8B02724D8
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D801D48
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,rbx
M82_L16:
       mov       rdx,rsi
       mov       rcx,7FF8B02AC378
       call      qword ptr [7FF8B0276490]; System.Linq.Enumerable.Aggregate[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, System.Func`3<System.__Canon,System.__Canon,System.__Canon>)
       nop
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M82_L17:
       mov       rcx,rsi
       xor       edx,edx
       call      qword ptr [7FF8AF72DBA0]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]].get_Item(Int32)
       nop
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M82_L18:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M82_L19:
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
M82_L20:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B05064D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M82_L21:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,0EC1
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF9666D0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 1056
```
```assembly
; Hl7.Fhir.ElementModel.NewPocoBuilder.setOrAddProperty(Hl7.Fhir.ElementModel.ITypedElement, Hl7.Fhir.Model.Base, Hl7.Fhir.Model.Base, Hl7.Fhir.Introspection.PropertyMapping)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,70
       lea       rbp,[rsp+0A0]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-60],ymm4
       vmovdqa   xmmword ptr [rbp-40],xmm4
       mov       [rbp-80],rsp
       mov       [rbp+18],rdx
       mov       [rbp+20],r8
       mov       [rbp+28],r9
       mov       rbx,rcx
       mov       r10,rdx
       mov       rax,r8
       mov       rsi,[rbp+30]
       test      rsi,rsi
       je        near ptr M83_L08
M83_L00:
       mov       rdi,[r10]
       mov       r14,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       rdi,r14
       jne       near ptr M83_L35
       mov       rcx,[r10+30]
       test      rcx,rcx
       je        near ptr M83_L13
       mov       r8,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],r8
       jne       near ptr M83_L33
       mov       rdx,[rcx+8]
M83_L01:
       test      rdx,rdx
       je        near ptr M83_L34
M83_L02:
       mov       r8,offset MT_Hl7.Fhir.Model.DynamicResource
       cmp       [rax],r8
       je        near ptr M83_L14
       lea       r8,[rbp-38]
       mov       rcx,rax
       mov       r11,[rax]
       mov       r11,[r11+48]
       call      qword ptr [r11+20]
M83_L03:
       xor       r15d,r15d
       test      eax,eax
       cmovne    r15,[rbp-38]
       mov       rcx,r15
       test      rcx,rcx
       je        short M83_L04
       mov       r8,offset MT_System.Collections.Generic.List<Hl7.Fhir.Model.FhirString>
       cmp       [rcx],r8
       jne       near ptr M83_L36
M83_L04:
       mov       [rbp-68],rcx
       test      rcx,rcx
       jne       near ptr M83_L19
       test      r15,r15
       jne       near ptr M83_L40
       cmp       rdi,r14
       jne       near ptr M83_L37
       mov       r10,[rbp+18]
       mov       rcx,[r10+30]
M83_L05:
       test      rcx,rcx
       je        short M83_L07
       mov       r8,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],r8
       jne       near ptr M83_L38
       movzx     r13d,byte ptr [rcx+68]
M83_L06:
       test      r13d,r13d
       jne       near ptr M83_L15
M83_L07:
       test      rsi,rsi
       je        near ptr M83_L21
       cmp       byte ptr [rsi+68],0
       je        near ptr M83_L21
       jmp       near ptr M83_L15
M83_L08:
       mov       rdi,[r10]
       mov       r14,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       rdi,r14
       jne       near ptr M83_L30
       mov       rcx,[r10+30]
M83_L09:
       test      rcx,rcx
       je        short M83_L12
       mov       r8,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],r8
       jne       near ptr M83_L31
       cmp       dword ptr [rcx+64],2
       sete      r15b
       movzx     r15d,r15b
M83_L10:
       movzx     r8d,r15b
       mov       ecx,1
M83_L11:
       test      cl,cl
       mov       r10,[rbp+18]
       je        near ptr M83_L00
       test      r8b,r8b
       je        near ptr M83_L00
       jmp       near ptr M83_L32
M83_L12:
       xor       ecx,ecx
       xor       r8d,r8d
       jmp       short M83_L11
M83_L13:
       xor       edx,edx
       jmp       near ptr M83_L01
M83_L14:
       lea       r8,[rbp-38]
       mov       rcx,rax
       call      qword ptr [7FF8AFC56AF8]; Hl7.Fhir.Model.DomainResource.TryGetValue(System.String, System.Object ByRef)
       jmp       near ptr M83_L03
M83_L15:
       mov       rcx,[rbp+28]
       call      System.Object.GetType()
       mov       rdx,rax
       test      rsi,rsi
       jne       short M83_L18
       mov       rcx,rbx
       call      qword ptr [7FF8B017DD40]; Hl7.Fhir.ElementModel.NewPocoBuilder.getClassMapping(System.Type)
       mov       rcx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0304FF0]; Hl7.Fhir.Introspection.ClassMapping.CreateList()
       mov       r13,rax
M83_L16:
       mov       rcx,r13
       mov       rdx,[rbp+28]
       mov       r11,7FF8AF572D30
       call      qword ptr [r11]
       cmp       rdi,r14
       jne       near ptr M83_L39
       mov       rcx,[rbp+18]
       call      qword ptr [7FF8B0075B90]; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.get_Name()
M83_L17:
       mov       rcx,[rbp+20]
       mov       rdx,rax
       mov       r8,r13
       mov       rax,[rcx]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       nop
       add       rsp,70
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M83_L18:
       mov       rdx,[rsi+28]
       mov       rcx,rbx
       call      qword ptr [7FF8B017DD40]; Hl7.Fhir.ElementModel.NewPocoBuilder.getClassMapping(System.Type)
       mov       rcx,rax
       cmp       [rcx],ecx
       call      qword ptr [7FF8B0304FF0]; Hl7.Fhir.Introspection.ClassMapping.CreateList()
       mov       r13,rax
       jmp       short M83_L16
M83_L19:
       mov       rdx,[rbp+28]
       mov       r11,7FF8AF572D80
       call      qword ptr [r11]
       jmp       near ptr M83_L43
M83_L20:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D70
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,[rbp+20]
       mov       r8,[rbp-70]
       mov       rax,[rcx]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       jmp       near ptr M83_L43
M83_L21:
       test      rsi,rsi
       je        short M83_L22
       cmp       byte ptr [rsi+69],0
       jne       short M83_L26
M83_L22:
       mov       r10,[rbp+18]
       cmp       rdi,r14
       jne       near ptr M83_L29
       mov       rcx,[r10+30]
       test      rcx,rcx
       je        short M83_L25
       mov       r10,[rbp+18]
       mov       rdx,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rcx],rdx
       jne       near ptr M83_L27
       mov       rdx,[rcx+8]
M83_L23:
       test      rdx,rdx
       je        near ptr M83_L28
M83_L24:
       mov       rcx,[rbp+20]
       mov       r8,[rbp+28]
       mov       rax,[rcx]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       jmp       near ptr M83_L43
M83_L25:
       mov       r10,[rbp+18]
       xor       edx,edx
       jmp       short M83_L23
M83_L26:
       mov       rdx,[rbp+28]
       mov       rcx,offset MT_Hl7.Fhir.Model.PrimitiveType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M83_L22
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+50]
       call      qword ptr [rax+20]
       mov       r15,rax
       test      r15,r15
       je        near ptr M83_L22
       mov       r10,[rbp+18]
       mov       rcx,r10
       mov       r11,7FF8AF572D48
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rcx,[rbp+20]
       mov       r8,r15
       mov       rax,[rcx]
       mov       rax,[rax+48]
       call      qword ptr [rax+18]
       jmp       near ptr M83_L43
M83_L27:
       mov       r11,7FF8AF572DB0
       call      qword ptr [r11]
       mov       rdx,rax
       mov       r10,[rbp+18]
       jmp       near ptr M83_L23
M83_L28:
       mov       rcx,[r10+18]
       mov       r11,7FF8AF572DB8
       call      qword ptr [r11]
       mov       rdx,rax
       mov       r10,[rbp+18]
       jmp       near ptr M83_L24
M83_L29:
       mov       rcx,r10
       mov       r11,7FF8AF572D40
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M83_L24
M83_L30:
       mov       rcx,r10
       mov       r11,7FF8AF572D90
       call      qword ptr [r11]
       mov       rcx,rax
       mov       rax,[rbp+20]
       jmp       near ptr M83_L09
M83_L31:
       mov       r11,7FF8AF572D98
       call      qword ptr [r11]
       mov       r15d,eax
       mov       rax,[rbp+20]
       jmp       near ptr M83_L10
M83_L32:
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.ChoiceElementAnnotation
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       r9,[rbp+28]
       cmp       [r9],r9b
       mov       rcx,r9
       call      qword ptr [7FF8B017EB08]; Hl7.Fhir.Model.Base.get_annotations()
       mov       rcx,rax
       mov       rdx,rdi
       cmp       [rcx],ecx
       call      qword ptr [7FF8AFF70638]; Hl7.Fhir.Utility.AnnotationList.AddAnnotation(System.Object)
       mov       rax,[rbp+20]
       mov       r10,[rbp+18]
       jmp       near ptr M83_L00
M83_L33:
       mov       r11,7FF8AF572DA0
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rax,[rbp+20]
       jmp       near ptr M83_L01
M83_L34:
       mov       r10,[rbp+18]
       mov       rcx,[r10+18]
       mov       r11,7FF8AF572DA8
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rax,[rbp+20]
       jmp       near ptr M83_L02
M83_L35:
       mov       rcx,r10
       mov       r11,7FF8AF572D18
       call      qword ptr [r11]
       mov       rdx,rax
       mov       rax,[rbp+20]
       jmp       near ptr M83_L02
M83_L36:
       mov       rdx,r15
       mov       rcx,offset MT_System.Collections.IList
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M83_L04
M83_L37:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D20
       call      qword ptr [r11]
       mov       rcx,rax
       mov       r10,[rbp+18]
       jmp       near ptr M83_L05
M83_L38:
       mov       r11,7FF8AF572D28
       call      qword ptr [r11]
       mov       r13d,eax
       mov       r10,[rbp+18]
       jmp       near ptr M83_L06
M83_L39:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D38
       call      qword ptr [r11]
       jmp       near ptr M83_L17
M83_L40:
       mov       rcx,[r15]
       mov       r9,[rbp+28]
       cmp       rcx,[r9]
       jne       short M83_L41
       mov       rcx,r15
       call      System.Object.GetType()
       jmp       short M83_L42
M83_L41:
       mov       rax,26D002DCF38
M83_L42:
       mov       rcx,rbx
       mov       rdx,rsi
       mov       r8,rax
       call      qword ptr [7FF8B0276808]; Hl7.Fhir.ElementModel.NewPocoBuilder.buildNewList(Hl7.Fhir.Introspection.PropertyMapping, System.Type)
       mov       [rbp-70],rax
       mov       rcx,[rbp-70]
       mov       rdx,r15
       mov       r11,7FF8AF572D60
       call      qword ptr [r11]
       mov       rcx,[rbp-70]
       mov       rdx,[rbp+28]
       mov       r11,7FF8AF572D68
       call      qword ptr [r11]
       jmp       near ptr M83_L20
M83_L43:
       add       rsp,70
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+0A0]
       lea       rcx,[rbp-60]
       mov       edx,38
       mov       r8d,3
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L52
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,1C
       jb        short M83_L44
       mov       rcx,26D003A9544
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+18]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+18],ymm1
       mov       ecx,[rbp-50]
       add       ecx,1C
       mov       [rbp-50],ecx
       jmp       short M83_L45
M83_L44:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A9538
       call      qword ptr [7FF8B039CB58]
M83_L45:
       mov       rcx,[rbp+28]
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L52
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,0F
       jb        short M83_L46
       mov       rcx,26D003A94DC
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+0E]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+0E],xmm1
       mov       ecx,[rbp-50]
       add       ecx,0F
       mov       [rbp-50],ecx
       jmp       short M83_L47
M83_L46:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A94D0
       call      qword ptr [7FF8B039CB58]
M83_L47:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D88
       call      qword ptr [r11]
       mov       rdx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L52
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,0B
       jb        short M83_L48
       mov       rcx,26D003A9514
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+6]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+6],xmm1
       mov       ecx,[rbp-50]
       add       ecx,0B
       mov       [rbp-50],ecx
       jmp       short M83_L49
M83_L48:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A9508
       call      qword ptr [7FF8B039CB58]
M83_L49:
       mov       rcx,[rbp-68]
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        short M83_L52
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,2
       jb        short M83_L50
       mov       rcx,26D0039E804
       mov       eax,[rcx]
       mov       [rdx],eax
       mov       ecx,[rbp-50]
       add       ecx,2
       mov       [rbp-50],ecx
       jmp       short M83_L51
M83_L50:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039E7F8
       call      qword ptr [7FF8B039CB58]
M83_L51:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M83_L52:
       call      qword ptr [7FF8AF827798]
       int       3
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+0A0]
       lea       rcx,[rbp-60]
       mov       edx,38
       mov       r8d,3
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L61
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,1C
       jb        short M83_L53
       mov       rcx,26D003A948C
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+18]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+18],ymm1
       mov       ecx,[rbp-50]
       add       ecx,1C
       mov       [rbp-50],ecx
       jmp       short M83_L54
M83_L53:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A9480
       call      qword ptr [7FF8B039CB58]
M83_L54:
       mov       rcx,[rbp-70]
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L61
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,0F
       jb        short M83_L55
       mov       rcx,26D003A94DC
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+0E]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+0E],xmm1
       mov       ecx,[rbp-50]
       add       ecx,0F
       mov       [rbp-50],ecx
       jmp       short M83_L56
M83_L55:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A94D0
       call      qword ptr [7FF8B039CB58]
M83_L56:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D78
       call      qword ptr [r11]
       mov       rdx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L61
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,0B
       jb        short M83_L57
       mov       rcx,26D003A9514
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+6]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+6],xmm1
       mov       ecx,[rbp-50]
       add       ecx,0B
       mov       [rbp-50],ecx
       jmp       short M83_L58
M83_L57:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A9508
       call      qword ptr [7FF8B039CB58]
M83_L58:
       mov       rcx,[rbp+20]
       call      System.Object.GetType()
       mov       r8,rax
       lea       rcx,[rbp-60]
       mov       rdx,7FF8B0246238
       call      qword ptr [7FF8AF82E028]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        short M83_L61
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,2
       jb        short M83_L59
       mov       rcx,26D0039E804
       mov       eax,[rcx]
       mov       [rdx],eax
       mov       ecx,[rbp-50]
       add       ecx,2
       mov       [rbp-50],ecx
       jmp       short M83_L60
M83_L59:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039E7F8
       call      qword ptr [7FF8B039CB58]
M83_L60:
       mov       rcx,offset MT_System.InvalidOperationException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF966E68]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M83_L61:
       call      qword ptr [7FF8AF827798]
       int       3
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       rbp,[rcx+20]
       mov       [rsp+20],rbp
       lea       rbp,[rbp+0A0]
       mov       rdx,[rbp+28]
       mov       rcx,offset MT_Hl7.Fhir.Model.IDynamicType
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       test      rax,rax
       jne       short M83_L62
       mov       rcx,[rbp+28]
       call      System.Object.GetType()
       mov       rcx,rax
       call      qword ptr [7FF8AF56A158]; System.RuntimeType.get_Name()
       mov       rbx,rax
       jmp       short M83_L63
M83_L62:
       mov       rcx,rax
       mov       r11,7FF8AF572D50
       call      qword ptr [r11]
       mov       rbx,rax
M83_L63:
       lea       rcx,[rbp-60]
       mov       edx,2B
       mov       r8d,2
       call      qword ptr [7FF8AF617FD8]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L70
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,1B
       jb        short M83_L64
       mov       rcx,26D003A9404
       vmovdqu   ymm0,ymmword ptr [rcx]
       vmovdqu   ymm1,ymmword ptr [rcx+16]
       vmovdqu   ymmword ptr [rdx],ymm0
       vmovdqu   ymmword ptr [rdx+16],ymm1
       mov       ecx,[rbp-50]
       add       ecx,1B
       mov       [rbp-50],ecx
       jmp       short M83_L65
M83_L64:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A93F8
       call      qword ptr [7FF8B039CB58]
M83_L65:
       lea       rcx,[rbp-60]
       mov       rdx,rbx
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        near ptr M83_L70
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,0E
       jb        short M83_L66
       mov       rcx,26D003A9454
       vmovdqu   xmm0,xmmword ptr [rcx]
       vmovdqu   xmm1,xmmword ptr [rcx+0C]
       vmovdqu   xmmword ptr [rdx],xmm0
       vmovdqu   xmmword ptr [rdx+0C],xmm1
       mov       ecx,[rbp-50]
       add       ecx,0E
       mov       [rbp-50],ecx
       jmp       short M83_L67
M83_L66:
       lea       rcx,[rbp-60]
       mov       rdx,26D003A9448
       call      qword ptr [7FF8B039CB58]
M83_L67:
       mov       rcx,[rbp+18]
       mov       r11,7FF8AF572D58
       call      qword ptr [r11]
       mov       rdx,rax
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF82E088]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       mov       ecx,[rbp-50]
       cmp       ecx,[rbp-40]
       ja        short M83_L70
       mov       rdx,[rbp-48]
       mov       eax,ecx
       lea       rdx,[rdx+rax*2]
       mov       eax,[rbp-40]
       sub       eax,ecx
       cmp       eax,2
       jb        short M83_L68
       mov       rcx,26D0039E804
       mov       eax,[rcx]
       mov       [rdx],eax
       mov       ecx,[rbp-50]
       add       ecx,2
       mov       [rbp-50],ecx
       jmp       short M83_L69
M83_L68:
       lea       rcx,[rbp-60]
       mov       rdx,26D0039E7F8
       call      qword ptr [7FF8B039CB58]
M83_L69:
       lea       rcx,[rbp-60]
       call      qword ptr [7FF8AF61C018]; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       mov       rcx,rax
       call      qword ptr [7FF8AFB878E8]
       mov       rcx,rax
       call      CORINFO_HELP_THROW
       int       3
M83_L70:
       call      qword ptr [7FF8AF827798]
       int       3
; Total bytes of code 2884
```
```assembly
; Hl7.Fhir.Model.Base+<>c.<get_annotations>b__9_0()
       push      rbx
       sub       rsp,20
       mov       rcx,offset MT_Hl7.Fhir.Utility.AnnotationList
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8B017EBB0]; Hl7.Fhir.Utility.AnnotationList..ctor()
       mov       rax,rbx
       add       rsp,20
       pop       rbx
       ret
; Total bytes of code 41
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementExtensions.Annotation[[System.__Canon, System.Private.CoreLib]](Hl7.Fhir.ElementModel.ITypedElement)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       xor       eax,eax
       mov       [rsp+20],rax
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        short M85_L00
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode
       cmp       [rsi],rcx
       jne       near ptr M85_L07
M85_L00:
       test      rsi,rsi
       je        near ptr M85_L11
       mov       rcx,[rbx+18]
       mov       rdi,[rcx+10]
       test      rdi,rdi
       je        near ptr M85_L04
M85_L01:
       mov       rcx,[rdi+18]
       mov       rcx,[rcx]
       call      CORINFO_HELP_TYPEHANDLE_TO_RUNTIMETYPE
       mov       rdx,rax
       mov       rcx,rsi
       mov       r11,7FF8AF573140
       call      qword ptr [r11]
       mov       rbx,rax
       test      rbx,rbx
       je        near ptr M85_L10
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Object>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rsi,rax
       test      rsi,rsi
       jne       short M85_L05
       lea       r8,[rsp+20]
       mov       rdx,rbx
       mov       rcx,7FF8B0076700
       call      qword ptr [7FF8AFEAFE10]; System.Linq.Enumerable.TryGetFirstNonIterator[[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IEnumerable`1<System.__Canon>, Boolean ByRef)
M85_L02:
       mov       rcx,[rdi+18]
       mov       rcx,[rcx]
       mov       r8,rax
       test      r8,r8
       je        short M85_L03
       cmp       [r8],rcx
       je        short M85_L03
       mov       rdx,rax
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r8,rax
M85_L03:
       mov       rax,r8
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M85_L04:
       mov       rcx,rbx
       mov       rdx,7FF8B0250EC0
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       rdi,rax
       jmp       near ptr M85_L01
M85_L05:
       mov       rcx,offset MT_System.Linq.Enumerable+IListSkipTakeIterator<Newtonsoft.Json.Linq.JProperty>
       cmp       [rsi],rcx
       jne       short M85_L09
       mov       rcx,[rsi+18]
       mov       r11,7FF8AF573148
       call      qword ptr [r11]
       cmp       eax,[rsi+20]
       jg        short M85_L08
       xor       edx,edx
       mov       [rsp+20],edx
       xor       eax,eax
M85_L06:
       jmp       short M85_L02
M85_L07:
       mov       rcx,offset MT_Hl7.Fhir.Utility.IAnnotated
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfInterface(Void*, System.Object)
       mov       rsi,rax
       jmp       near ptr M85_L00
M85_L08:
       mov       dword ptr [rsp+20],1
       mov       rcx,[rsi+18]
       mov       edx,[rsi+20]
       mov       r11,7FF8AF573150
       call      qword ptr [r11]
       jmp       short M85_L06
M85_L09:
       lea       rdx,[rsp+20]
       mov       rcx,rsi
       mov       rax,[rsi]
       mov       rax,[rax+48]
       call      qword ptr [rax+10]
       jmp       short M85_L06
M85_L10:
       mov       ecx,11
       call      qword ptr [7FF8AF61F738]
       int       3
M85_L11:
       xor       eax,eax
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 378
```
```assembly
; Hl7.Fhir.Serialization.PrimitiveTypeConverter.ConvertTo(System.Object, System.Type)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,rdx
       test      rsi,rsi
       je        near ptr M86_L04
       test      rbx,rbx
       je        short M86_L03
       mov       rcx,rbx
       call      System.Object.GetType()
       cmp       rax,rsi
       je        short M86_L02
       mov       rcx,26D002D0020
       cmp       rsi,rcx
       jne       short M86_L00
       mov       rcx,rbx
       add       rsp,28
       pop       rbx
       pop       rsi
       jmp       qword ptr [7FF8B0305140]; Hl7.Fhir.Serialization.PrimitiveTypeConverter.convertToXmlString(System.Object)
M86_L00:
       mov       rdx,rbx
       mov       rcx,offset MT_System.String
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rax,rax
       je        short M86_L01
       mov       rcx,rsi
       mov       rdx,rax
       add       rsp,28
       pop       rbx
       pop       rsi
       jmp       qword ptr [7FF8B0305158]
M86_L01:
       mov       rcx,rbx
       mov       rdx,rsi
       xor       r8d,r8d
       add       rsp,28
       pop       rbx
       pop       rsi
       jmp       qword ptr [7FF8B0305170]
M86_L02:
       mov       rax,rbx
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M86_L03:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,2181
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF9666D0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M86_L04:
       mov       rcx,offset MT_System.ArgumentNullException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       ecx,1022D
       mov       rdx,7FF8AF8B52B8
       call      CORINFO_HELP_STRCNS
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF9666D0]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 259
```
```assembly
; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       xor       ecx,ecx
       mov       [rbx],rcx
       mov       rcx,26D3D8002B0
       mov       rcx,[rcx]
       imul      eax,r8d,0B
       add       edx,eax
       mov       eax,100
       cmp       edx,100
       cmovle    edx,eax
       call      qword ptr [7FF8AF83BB48]; System.Buffers.SharedArrayPool`1[[System.Char, System.Private.CoreLib]].Rent(Int32)
       mov       [rbx+8],rax
       test      rax,rax
       je        short M87_L01
       lea       rcx,[rax+10]
       mov       eax,[rax+8]
M87_L00:
       mov       [rbx+18],rcx
       mov       [rbx+20],eax
       xor       ecx,ecx
       mov       [rbx+10],ecx
       mov       byte ptr [rbx+14],0
       add       rsp,20
       pop       rbx
       ret
M87_L01:
       xor       ecx,ecx
       xor       eax,eax
       jmp       short M87_L00
; Total bytes of code 96
```
```assembly
; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted[[System.__Canon, System.Private.CoreLib]](System.__Canon)
       push      rsi
       push      rbx
       sub       rsp,68
       xorps     xmm4,xmm4
       movaps    [rsp+30],xmm4
       movaps    [rsp+40],xmm4
       xor       eax,eax
       mov       [rsp+50],rax
       mov       [rsp+60],rdx
       mov       [rsp+90],r8
       mov       rbx,rcx
       mov       rcx,rdx
       cmp       byte ptr [rbx+14],0
       jne       near ptr M88_L15
       mov       rcx,[rsp+90]
       call      qword ptr [7FF90B37E688]
       test      rax,rax
       je        near ptr M88_L05
       mov       rcx,[rsp+90]
       call      qword ptr [7FF90B37E6C0]
       test      rax,rax
       je        near ptr M88_L12
M88_L00:
       mov       rcx,[rsp+90]
       call      qword ptr [7FF90B37F840]
       lea       rdx,[rbx+18]
       mov       r9d,[rbx+10]
       mov       r10d,[rdx+8]
       cmp       r9d,r10d
       ja        near ptr M88_L14
       mov       rdx,[rdx]
       mov       r8d,r9d
       lea       r11,[rdx+r8*2]
       sub       r10d,r9d
       xor       edx,edx
       mov       r9,[rbx]
       mov       r8,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       cmp       r8,[rax]
       je        short M88_L02
       mov       [rsp+40],r11
       mov       [rsp+48],r10d
       mov       [rsp+30],rdx
       xor       edx,edx
       mov       [rsp+38],edx
       mov       [rsp+20],r9
       lea       rdx,[rsp+40]
       lea       r9,[rsp+30]
       lea       r8,[rsp+58]
       mov       rcx,rax
       lea       r11,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       call      qword ptr [r11]
M88_L01:
       test      eax,eax
       je        near ptr M88_L11
       mov       edx,[rsp+58]
       add       [rbx+10],edx
       add       rsp,68
       pop       rbx
       pop       rsi
       ret
M88_L02:
       cmp       dword ptr [rax+10],0FFFFFFFF
       je        short M88_L04
       cmp       dword ptr [rax+14],0FFFFFFFF
       je        near ptr M88_L10
       mov       r8d,4
M88_L03:
       mov       [rsp+40],r11
       mov       [rsp+48],r10d
       lea       rdx,[rsp+40]
       lea       r9,[rsp+58]
       mov       rcx,rax
       call      qword ptr [7FF90B397E20]; Precode of System.Version.TryFormatCore[[System.Char, System.Private.CoreLib]](System.Span`1<Char>, Int32, Int32 ByRef)
       jmp       short M88_L01
M88_L04:
       mov       r8d,2
       jmp       short M88_L03
M88_L05:
       xor       ecx,ecx
       mov       [rsp+50],rcx
       lea       rcx,[rsp+90]
       cmp       qword ptr [rsp+50],0
       jne       short M88_L06
       mov       rcx,[rsp+90]
       mov       [rsp+50],rcx
       lea       rcx,[rsp+50]
       cmp       qword ptr [rsp+50],0
       je        near ptr M88_L13
M88_L06:
       mov       rcx,[rcx]
       lea       r11,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       cmp       [rcx],ecx
       call      qword ptr [r11]
       mov       rdx,rax
M88_L07:
       test      rdx,rdx
       je        short M88_L08
       lea       r8,[rbx+18]
       mov       ecx,[rbx+10]
       mov       eax,[r8+8]
       cmp       ecx,eax
       ja        near ptr M88_L14
       mov       r8,[r8]
       mov       r10d,ecx
       lea       r10,[r8+r10*2]
       sub       eax,ecx
       mov       esi,[rdx+8]
       cmp       esi,eax
       ja        short M88_L09
       mov       r8d,esi
       add       r8,r8
       add       rdx,0C
       mov       rcx,r10
       call      qword ptr [7FF90B384C58]; Precode of System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       add       [rbx+10],esi
M88_L08:
       add       rsp,68
       pop       rbx
       pop       rsi
       ret
M88_L09:
       mov       rcx,rbx
       call      qword ptr [7FF90B38BDD8]
       jmp       short M88_L08
M88_L10:
       mov       r8d,3
       jmp       near ptr M88_L03
M88_L11:
       mov       rcx,rbx
       call      qword ptr [7FF90B38BDF8]
       jmp       near ptr M88_L00
M88_L12:
       mov       rcx,[rsp+90]
       call      qword ptr [7FF90B37F838]
       mov       rcx,rax
       mov       r8,[rbx]
       lea       r11,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       xor       edx,edx
       call      qword ptr [r11]
       mov       rdx,rax
       jmp       near ptr M88_L07
M88_L13:
       xor       edx,edx
       jmp       near ptr M88_L07
M88_L14:
       call      qword ptr [7FF90B3865E8]
       int       3
M88_L15:
       call      qword ptr [7FF90B377A38]
       mov       rdx,rax
       mov       rcx,rbx
       mov       r8,[rsp+90]
       xor       r9d,r9d
       call      qword ptr [7FF90B39B870]
       nop
       add       rsp,68
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 573
```
```assembly
; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(System.String)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       cmp       byte ptr [rbx+14],0
       jne       short M89_L01
       test      rdx,rdx
       je        short M89_L01
       lea       r8,[rbx+18]
       mov       ecx,[rbx+10]
       mov       eax,[r8+8]
       cmp       ecx,eax
       ja        short M89_L00
       mov       r8,[r8]
       mov       r10d,ecx
       lea       r10,[r8+r10*2]
       sub       eax,ecx
       mov       esi,[rdx+8]
       cmp       esi,eax
       ja        short M89_L01
       mov       r8d,esi
       add       r8,r8
       add       rdx,0C
       mov       rcx,r10
       call      qword ptr [7FF8AF6157B8]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       add       [rbx+10],esi
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M89_L00:
       call      qword ptr [7FF8AF827798]
       int       3
M89_L01:
       mov       rcx,rbx
       call      qword ptr [7FF8B045E880]
       nop
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 107
```
```assembly
; System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()
       push      rsi
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+28],rax
       mov       rbx,rcx
       lea       rcx,[rbx+18]
       mov       eax,[rbx+10]
       cmp       eax,[rcx+8]
       ja        short M90_L01
       mov       rcx,[rcx]
       mov       [rsp+28],rcx
       mov       [rsp+30],eax
       lea       rcx,[rsp+28]
       call      00007FF8AF820FC0
       mov       rsi,rax
       mov       rdx,[rbx+8]
       xor       ecx,ecx
       mov       [rbx],rcx
       mov       [rbx+8],rcx
       mov       [rbx+10],rcx
       mov       [rbx+18],rcx
       mov       [rbx+20],rcx
       test      rdx,rdx
       je        short M90_L00
       mov       rcx,26D3D8002B0
       mov       rcx,[rcx]
       xor       r8d,r8d
       call      qword ptr [7FF8AF83BB50]; System.Buffers.SharedArrayPool`1[[System.Char, System.Private.CoreLib]].Return(Char[], Boolean)
M90_L00:
       mov       rax,rsi
       add       rsp,38
       pop       rbx
       pop       rsi
       ret
M90_L01:
       call      qword ptr [7FF8AF827798]
       int       3
; Total bytes of code 122
```
```assembly
; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor()
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rdx,[rdx+0D0]
       test      rdx,rdx
       je        short M91_L01
M91_L00:
       mov       rcx,rdx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rdx,[rax]
       lea       rcx,[rbx+8]
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,30
       pop       rbx
       ret
M91_L01:
       mov       rdx,7FF8B043D0C0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
       jmp       short M91_L00
; Total bytes of code 82
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<>c.<Children>b__36_0(Hl7.Fhir.Specification.IElementDefinitionSummary)
       mov       rax,offset MT_Hl7.Fhir.Introspection.PropertyMapping
       cmp       [rdx],rax
       jne       short M92_L00
       mov       rax,[rdx+8]
       ret
M92_L00:
       mov       rcx,rdx
       mov       r11,7FF8AF5727E0
       jmp       qword ptr [r11]
; Total bytes of code 36
```
```assembly
; Hl7.Fhir.ElementModel.TypedElementOnSourceNode.enumerateElements(System.Collections.Generic.Dictionary`2<System.String,Hl7.Fhir.Specification.IElementDefinitionSummary>, Hl7.Fhir.ElementModel.ISourceNode, System.String)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       rbp,r9
       mov       rcx,offset MT_Hl7.Fhir.ElementModel.TypedElementOnSourceNode+<enumerateElements>d__35
       call      CORINFO_HELP_NEWSFAST
       mov       r14,rax
       mov       dword ptr [r14+58],0FFFFFFFE
       call      CORINFO_HELP_GETCURRENTMANAGEDTHREADID
       mov       [r14+5C],eax
       lea       rcx,[r14+40]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r14+38]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r14+28]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[r14+18]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,r14
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
; Total bytes of code 119
```
```assembly
; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]].set_Capacity(Int32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       mov       [rsp+28],rcx
       mov       rbx,rcx
       mov       esi,edx
       mov       edi,[rbx+10]
       cmp       esi,edi
       jl        near ptr M94_L08
       mov       rcx,[rbx+8]
       cmp       [rcx+8],esi
       je        near ptr M94_L07
       test      esi,esi
       jle       short M94_L04
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+0C8]
       test      rax,rax
       je        short M94_L02
       mov       rcx,rax
M94_L00:
       movsxd    rdx,esi
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       rsi,rax
       test      edi,edi
       jg        short M94_L03
M94_L01:
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M94_L02:
       mov       rdx,7FF8B043D0B8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       short M94_L00
M94_L03:
       mov       rcx,[rbx+8]
       mov       r8d,edi
       mov       rdx,rsi
       call      qword ptr [7FF8AF61F390]; System.Array.Copy(System.Array, System.Array, Int32)
       jmp       short M94_L01
M94_L04:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rdx,[rdx+0D0]
       test      rdx,rdx
       je        short M94_L05
       jmp       short M94_L06
M94_L05:
       mov       rdx,7FF8B043D0C0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdx,rax
M94_L06:
       mov       rcx,rdx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rdx,[rax]
       lea       rcx,[rbx+8]
       call      CORINFO_HELP_ASSIGN_REF
M94_L07:
       nop
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M94_L08:
       mov       ecx,7
       mov       edx,0F
       call      qword ptr [7FF8AFB84A20]
       int       3
; Total bytes of code 232
```
```assembly
; System.Array.Clear(System.Array, Int32, Int32)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       test      rcx,rcx
       je        near ptr M95_L04
       lea       rbx,[rcx+10]
       xor       esi,esi
       mov       rdi,[rcx]
       cmp       dword ptr [rdi+4],18
       ja        short M95_L01
M95_L00:
       mov       eax,edx
       sub       eax,esi
       cmp       edx,esi
       jl        short M95_L03
       mov       edx,eax
       or        edx,r8d
       jl        short M95_L03
       lea       edx,[rax+r8]
       cmp       edx,[rcx+8]
       ja        short M95_L03
       movzx     edx,word ptr [rdi]
       mov       ecx,eax
       imul      rcx,rdx
       add       rcx,rbx
       mov       eax,r8d
       imul      rdx,rax
       test      dword ptr [rdi],1000000
       je        short M95_L02
       shr       rdx,3
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       jmp       qword ptr [7FF8AFEA6D78]; System.SpanHelpers.ClearWithReferences(IntPtr ByRef, UIntPtr)
M95_L01:
       mov       eax,[rdi+4]
       add       eax,0FFFFFFE8
       shr       eax,3
       movsxd    r10,eax
       mov       esi,[rbx+r10*4]
       shl       eax,3
       cdqe
       add       rbx,rax
       jmp       short M95_L00
M95_L02:
       call      qword ptr [7FF8AF615788]; System.SpanHelpers.ClearWithoutReferences(Byte ByRef, UIntPtr)
       nop
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M95_L03:
       call      qword ptr [7FF8B050C570]
       int       3
M95_L04:
       mov       ecx,2
       call      qword ptr [7FF8AF61FB28]
       int       3
; Total bytes of code 159
```
```assembly
; Hl7.Fhir.Utility.AnnotationList..ctor()
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rcx,26D3D803678
       mov       rsi,[rcx]
       test      rsi,rsi
       je        short M96_L01
M96_L00:
       mov       rcx,offset MT_System.Lazy<System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>>
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       test      rsi,rsi
       je        near ptr M96_L02
       lea       rcx,[rdi+10]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rcx,offset MT_System.LazyHelper
       call      CORINFO_HELP_NEWSFAST
       mov       dword ptr [rax+10],8
       lea       rcx,[rdi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbx+8]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M96_L01:
       mov       rcx,offset MT_System.Func<System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.List<System.Object>>>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,26D3D803668
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Hl7.Fhir.Utility.AnnotationList+<>c.<.ctor>b__13_0()
       call      qword ptr [7FF8AF6169D0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,26D3D803678
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M96_L00
M96_L02:
       mov       ecx,23B3
       mov       rdx,7FF8AF564000
       call      CORINFO_HELP_STRCNS
       mov       rcx,rax
       call      qword ptr [7FF8B0506550]
       int       3
; Total bytes of code 225
```
```assembly
; System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       xor       eax,eax
       mov       [rsp+20],rax
       mov       [rsp+28],rcx
       mov       rbx,rdx
       mov       rsi,r8
       mov       rdi,r9
       test      rbx,rbx
       je        short M97_L03
       mov       rdx,[rcx+18]
       mov       r11,[rdx+20]
       test      r11,r11
       je        short M97_L01
M97_L00:
       lea       r8,[rsp+20]
       mov       rcx,rbx
       mov       rdx,rsi
       call      qword ptr [r11]
       test      eax,eax
       jne       short M97_L02
       mov       rcx,7FF8B0532404
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,rdi
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M97_L01:
       mov       rdx,7FF8B04397D8
       call      CORINFO_HELP_RUNTIMEHANDLE_METHOD
       mov       r11,rax
       jmp       short M97_L00
M97_L02:
       mov       rcx,7FF8B0532408
       call      CORINFO_HELP_COUNTPROFILE32
       mov       rax,[rsp+20]
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M97_L03:
       mov       rcx,7FF8B0532400
       call      CORINFO_HELP_COUNTPROFILE32
       mov       ecx,1
       call      qword ptr [7FF8AF61FB28]
       int       3
; Total bytes of code 165
```
```assembly
; System.TimeZoneInfo.GetUtcOffsetFromUtc(System.DateTime, System.TimeZoneInfo, Boolean ByRef, Boolean ByRef)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,48
       mov       rdi,rcx
       mov       rbx,rdx
       mov       rsi,r8
       mov       rbp,r9
       mov       byte ptr [rsi],0
       mov       byte ptr [rbp],0
       mov       r14,[rbx+40]
       mov       r15,3FFFFFFFFFFFFFFF
       and       r15,rdi
       mov       r9,2BCA27ACC9CD8000
       cmp       r15,r9
       jg        near ptr M98_L06
       mov       r9,0C92A69C000
       cmp       r15,r9
       jl        near ptr M98_L05
       lea       r9,[rsp+40]
       mov       rcx,rbx
       mov       rdx,rdi
       mov       r8d,1
       call      qword ptr [7FF8B0396010]; System.TimeZoneInfo.GetAdjustmentRuleForTime(System.DateTime, Boolean, System.Nullable`1<Int32> ByRef)
       mov       r13,rax
       add       r15,r14
       mov       rax,2BCA2875F4373FFF
       cmp       r15,rax
       ja        near ptr M98_L04
       mov       rdx,0C000000000000000
       and       rdx,rdi
       or        rdx,r15
       mov       rax,3FFFFFFFFFFFFFFF
       and       rdx,rax
       mov       rcx,28B8FFC778816079
       mov       rax,rdx
       mul       rcx
       shr       rdx,23
       or        edx,3
       mov       ecx,edx
       imul      rcx,396B06BD
       shr       rcx,2F
       imul      eax,ecx,23AB1
       sub       edx,eax
       or        edx,3
       mov       eax,edx
       imul      rax,166DB073
       shr       rax,27
       imul      ecx,64
       lea       r15d,[rax+rcx+1]
M98_L00:
       test      r13,r13
       je        near ptr M98_L03
       mov       rcx,[r13+58]
       lea       r12,[r14+rcx]
       sar       r14,3F
       sar       rcx,3F
       cmp       r14,rcx
       je        near ptr M98_L07
M98_L01:
       mov       r14,r12
       mov       rcx,r13
       call      qword ptr [7FF8B0395F08]; System.TimeZoneInfo+AdjustmentRule.get_HasDaylightSaving()
       test      eax,eax
       je        short M98_L03
       mov       r8,[rbx+40]
       mov       rcx,[rsp+40]
       mov       [rsp+38],rcx
       mov       [rsp+28],rbp
       mov       [rsp+30],rbx
       mov       rcx,[rsp+38]
       mov       [rsp+20],rcx
       mov       rcx,rdi
       mov       r9,r13
       mov       edx,r15d
       call      qword ptr [7FF8B0396058]; System.TimeZoneInfo.GetIsDaylightSavingsFromUtc(System.DateTime, Int32, System.TimeSpan, AdjustmentRule, System.Nullable`1<Int32>, Boolean ByRef, System.TimeZoneInfo)
       mov       [rsi],al
       cmp       byte ptr [rsi],0
       jne       near ptr M98_L08
       xor       r15d,r15d
M98_L02:
       lea       r14,[r12+r15]
       sar       r12,3F
       sar       r15,3F
       cmp       r12,r15
       je        near ptr M98_L09
M98_L03:
       mov       rax,r14
       add       rsp,48
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M98_L04:
       mov       ecx,1
       call      qword ptr [7FF8B0507300]
       int       3
M98_L05:
       lea       r9,[rsp+40]
       mov       rcx,rbx
       xor       edx,edx
       xor       r8d,r8d
       call      qword ptr [7FF8B0396010]; System.TimeZoneInfo.GetAdjustmentRuleForTime(System.DateTime, Boolean, System.Nullable`1<Int32> ByRef)
       mov       r13,rax
       mov       r15d,1
       jmp       near ptr M98_L00
M98_L06:
       lea       r9,[rsp+40]
       mov       rcx,rbx
       mov       rdx,2BCA2875F4373FFF
       xor       r8d,r8d
       call      qword ptr [7FF8B0396010]; System.TimeZoneInfo.GetAdjustmentRuleForTime(System.DateTime, Boolean, System.Nullable`1<Int32> ByRef)
       mov       r13,rax
       mov       r15d,270F
       jmp       near ptr M98_L00
M98_L07:
       mov       rax,r12
       sar       rax,3F
       cmp       r14,rax
       je        near ptr M98_L01
       jmp       short M98_L10
M98_L08:
       mov       r15,[r13+20]
       jmp       near ptr M98_L02
M98_L09:
       mov       rax,r14
       sar       rax,3F
       cmp       r12,rax
       je        near ptr M98_L03
M98_L10:
       call      qword ptr [7FF8B0507A20]
       int       3
; Total bytes of code 525
```
```assembly
; System.TimeZoneInfo+CachedData.GetCorrespondingKind(System.TimeZoneInfo)
       mov       rax,26D3D804270
       cmp       rdx,[rax]
       je        short M99_L00
       mov       eax,2
       xor       r8d,r8d
       cmp       rdx,[rcx+8]
       cmovne    eax,r8d
       ret
M99_L00:
       mov       eax,1
       ret
; Total bytes of code 38
```
```assembly
; System.Collections.HashHelpers.GetPrime(Int32)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       ebx,ecx
       test      ebx,ebx
       jl        short M100_L05
       mov       rcx,7FF90A79A8B8
       xor       edx,edx
       mov       r8d,48
M100_L00:
       mov       eax,[rcx+rdx]
       cmp       eax,ebx
       jl        short M100_L01
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M100_L01:
       add       rdx,4
       dec       r8d
       jne       short M100_L00
       mov       esi,ebx
       or        esi,1
M100_L02:
       cmp       esi,7FFFFFFF
       jge       short M100_L04
       mov       ecx,esi
       call      qword ptr [7FF8B0505DD0]
       test      eax,eax
       je        short M100_L03
       lea       ecx,[rsi-1]
       mov       edx,288DF0CB
       mov       eax,edx
       imul      ecx
       mov       eax,edx
       shr       eax,1F
       sar       edx,4
       add       eax,edx
       imul      eax,65
       sub       ecx,eax
       je        short M100_L03
       mov       eax,esi
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M100_L03:
       add       esi,2
       jmp       short M100_L02
M100_L04:
       mov       eax,ebx
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
M100_L05:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF8B0505DE8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8AF965608]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
; Total bytes of code 175
```
```assembly
; System.Nullable`1[[System.Int32, System.Private.CoreLib]].get_Value()
       sub       rsp,28
       cmp       byte ptr [rcx],0
       je        short M101_L00
       mov       eax,[rcx+4]
       add       rsp,28
       ret
M101_L00:
       call      qword ptr [7FF90B386808]
       int       3
; Total bytes of code 24
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Resize()
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       ecx,[rbx+38]
       call      qword ptr [7FF8AF61F360]; System.Collections.HashHelpers.ExpandPrime(Int32)
       mov       edx,eax
       mov       rcx,rbx
       xor       r8d,r8d
       add       rsp,20
       pop       rbx
       jmp       qword ptr [7FF8AF967210]; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Resize(Int32, Boolean)
; Total bytes of code 36
```
```assembly
; System.Collections.Generic.Dictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Resize(Int32, Boolean)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       esi,edx
       mov       edi,r8d
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+90]
       test      rax,rax
       je        near ptr M103_L05
       mov       rcx,rax
M103_L00:
       movsxd    rbp,esi
       mov       rdx,rbp
       call      CORINFO_HELP_NEWARR_1_VC
       mov       r14,rax
       mov       r15d,[rbx+38]
       mov       rcx,[rbx+10]
       mov       rdx,r14
       mov       r8d,r15d
       call      qword ptr [7FF8AF61F390]; System.Array.Copy(System.Array, System.Array, Int32)
       movzx     edx,dil
       test      dl,1
       jne       near ptr M103_L08
M103_L01:
       mov       rdx,rbp
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rbx+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,0FFFFFFFFFFFFFFFF
       mov       ecx,esi
       xor       edx,edx
       div       rcx
       inc       rax
       mov       [rbx+30],rax
       xor       esi,esi
       test      r15d,r15d
       jle       short M103_L04
       cmp       [r14+8],r15d
       jl        near ptr M103_L06
M103_L02:
       mov       ecx,esi
       lea       rcx,[rcx+rcx*2]
       cmp       dword ptr [r14+rcx*8+24],0FFFFFFFF
       jl        short M103_L03
       mov       ebp,[r14+rcx*8+20]
       mov       rdi,[rbx+8]
       mov       eax,ebp
       imul      rax,[rbx+30]
       shr       rax,20
       inc       rax
       mov       edx,[rdi+8]
       mov       r8d,edx
       imul      rax,r8
       shr       rax,20
       cmp       eax,edx
       jae       near ptr M103_L15
       mov       edx,eax
       lea       r13,[rdi+rdx*4+10]
       mov       edx,[r13]
       dec       edx
       mov       [r14+rcx*8+24],edx
       lea       ecx,[rsi+1]
       mov       [r13],ecx
M103_L03:
       inc       esi
       cmp       esi,r15d
       jl        short M103_L02
M103_L04:
       lea       rcx,[rbx+10]
       mov       rdx,r14
       call      CORINFO_HELP_ASSIGN_REF
       nop
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M103_L05:
       mov       rdx,7FF8B0590C98
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M103_L00
M103_L06:
       cmp       esi,[r14+8]
       jae       near ptr M103_L15
       mov       ecx,esi
       lea       rcx,[rcx+rcx*2]
       lea       rcx,[r14+rcx*8+10]
       cmp       dword ptr [rcx+14],0FFFFFFFF
       jl        short M103_L07
       mov       ebp,[rcx+10]
       mov       rdi,[rbx+8]
       mov       eax,ebp
       imul      rax,[rbx+30]
       shr       rax,20
       inc       rax
       mov       edx,[rdi+8]
       imul      rax,rdx
       shr       rax,20
       cmp       eax,[rdi+8]
       jae       near ptr M103_L15
       mov       edx,eax
       lea       r13,[rdi+rdx*4+10]
       mov       edx,[r13]
       dec       edx
       mov       [rcx+14],edx
       lea       ecx,[rsi+1]
       mov       [r13],ecx
M103_L07:
       inc       esi
       cmp       esi,r15d
       jl        short M103_L06
       jmp       near ptr M103_L04
M103_L08:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rdi,[rdx+80]
       test      rdi,rdi
       je        short M103_L09
       jmp       short M103_L10
M103_L09:
       mov       rdx,7FF8B043A430
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rdi,rax
M103_L10:
       mov       rdx,[rbx+18]
       mov       rcx,offset MT_System.Collections.Generic.NonRandomizedStringEqualityComparer
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       mov       rcx,rax
       mov       rax,[rax]
       mov       rax,[rax+40]
       call      qword ptr [rax+30]
       mov       rdx,rax
       mov       rcx,rdi
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       rdi,rax
       lea       rcx,[rbx+18]
       mov       rdx,rdi
       call      CORINFO_HELP_ASSIGN_REF
       xor       r13d,r13d
M103_L11:
       cmp       r13d,r15d
       jge       near ptr M103_L01
       cmp       r13d,[r14+8]
       jae       short M103_L15
       lea       rcx,[r13+r13*2]
       cmp       dword ptr [r14+rcx*8+24],0FFFFFFFF
       jl        short M103_L14
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       r11,[rdx+68]
       test      r11,r11
       je        short M103_L12
       jmp       short M103_L13
M103_L12:
       mov       rdx,7FF8B04374B8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
M103_L13:
       lea       r12,[r13+r13*2]
       lea       rdx,[r13+r13*2]
       mov       rdx,[r14+rdx*8+10]
       mov       rcx,rdi
       call      qword ptr [r11]
       mov       [r14+r12*8+20],eax
M103_L14:
       inc       r13d
       jmp       short M103_L11
M103_L15:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 630
```
```assembly
; System.RuntimeType.InitializeCache()
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,98
       vzeroupper
       lea       rbp,[rsp+0D0]
       vxorps    xmm4,xmm4,xmm4
       vmovdqa   xmmword ptr [rbp-50],xmm4
       xor       eax,eax
       mov       [rbp-40],rax
       mov       rbx,rcx
       lea       rcx,[rbp-88]
       call      CORINFO_HELP_INIT_PINVOKE_FRAME
       mov       rsi,rax
       mov       rcx,rsp
       mov       [rbp-70],rcx
       mov       rcx,rbp
       mov       [rbp-60],rcx
       cmp       qword ptr [rbx+10],0
       je        near ptr M104_L07
M104_L00:
       mov       rcx,[rbx+10]
       mov       rdx,[rcx]
       mov       rdi,rdx
       test      rdi,rdi
       je        short M104_L01
       mov       rcx,offset MT_System.RuntimeType+RuntimeTypeCache
       cmp       [rdi],rcx
       jne       near ptr M104_L09
M104_L01:
       test      rdi,rdi
       jne       near ptr M104_L06
       mov       rcx,offset MT_System.RuntimeType+RuntimeTypeCache
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       mov       [rbp-0A8],rdi
       xor       ecx,ecx
       mov       [rdi+90],ecx
       lea       rcx,[rdi+8]
       mov       rdx,rbx
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbp+10],rbx
       mov       rcx,rbx
       call      System.RuntimeTypeHandle.GetModule(System.RuntimeType)
       mov       r14,rax
       mov       [rbp-0B0],r14
       mov       rax,[r14+8]
       test      rax,rax
       jne       near ptr M104_L04
       mov       [rbp-50],r14
       xor       ecx,ecx
       mov       [rbp-48],rcx
       mov       rcx,[rbp-50]
       mov       rcx,[rcx+18]
       lea       rdx,[rbp-50]
       mov       [rbp-0A0],rdx
       mov       [rbp-98],rcx
       lea       rcx,[rbp-0A0]
       lea       rdx,[rbp-48]
       mov       rax,7FF8AF8A9838
       mov       [rbp-78],rax
       lea       rax,[M104_L02]
       mov       [rbp-68],rax
       lea       rax,[rbp-88]
       mov       [rsi+8],rax
       mov       byte ptr [rsi+4],0
       mov       rax,7FF90F134C90
       call      rax
M104_L02:
       mov       byte ptr [rsi+4],1
       cmp       dword ptr [7FF90F57C744],0
       je        short M104_L03
       call      qword ptr [7FF90F56A418]; CORINFO_HELP_STOP_FOR_GC
M104_L03:
       mov       rcx,[rbp-80]
       mov       [rsi+8],rcx
       mov       rsi,[rbp-48]
       xor       ecx,ecx
       mov       [rbp-48],rcx
       mov       r14,[rbp-0B0]
       lea       rcx,[r14+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       rax,rsi
M104_L04:
       mov       rbx,[rbp+10]
       cmp       rax,rbx
       sete      cl
       mov       rdi,[rbp-0A8]
       mov       [rdi+94],cl
       mov       rcx,[rbx+10]
       mov       rdx,rdi
       xor       r8d,r8d
       call      System.Runtime.InteropServices.GCHandle.InternalCompareExchange(IntPtr, System.Object, System.Object)
       mov       rdx,rax
       mov       rax,rdx
       test      rax,rax
       je        short M104_L05
       mov       rcx,offset MT_System.RuntimeType+RuntimeTypeCache
       cmp       [rax],rcx
       jne       short M104_L08
M104_L05:
       test      rax,rax
       cmovne    rdi,rax
M104_L06:
       mov       rax,rdi
       add       rsp,98
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M104_L07:
       mov       [rbp-40],rbx
       lea       rcx,[rbp-40]
       mov       edx,1
       call      qword ptr [7FF8B0824DC8]; System.RuntimeTypeHandle.GetGCHandle(System.Runtime.InteropServices.GCHandleType)
       mov       rdx,rax
       lea       rcx,[rbx+10]
       xor       eax,eax
       lock cmpxchg [rcx],rdx
       test      rax,rax
       je        near ptr M104_L00
       lea       rcx,[rbp-40]
       call      qword ptr [7FF8B050DE78]
       jmp       near ptr M104_L00
M104_L08:
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
M104_L09:
       call      System.Runtime.CompilerServices.CastHelpers.ChkCastClass(Void*, System.Object)
       int       3
; Total bytes of code 532
```
```assembly
; System.RuntimeType+IGenericCacheEntry`1[[System.__Canon, System.Private.CoreLib]].CreateAndCache(System.RuntimeType)
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
M105_L00:
       mov       rcx,[rsi+10]
       test      rcx,rcx
       je        near ptr M105_L13
       mov       rdi,[rcx]
       test      rdi,rdi
       je        near ptr M105_L13
M105_L01:
       cmp       [rdi],dil
       add       rdi,78
       mov       rbp,[rdi]
       test      rbp,rbp
       jne       short M105_L04
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       rax,[rcx+20]
       test      rax,rax
       je        short M105_L03
M105_L02:
       mov       rcx,rsi
       call      rax
       mov       rbp,rax
       mov       rcx,rdi
       mov       rdx,rbp
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       test      rax,rax
       jne       short M105_L00
       mov       rax,rbp
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M105_L03:
       mov       rcx,rbx
       mov       rdx,7FF8AF941F10
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       short M105_L02
M105_L04:
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       rcx,[rcx]
       mov       rdx,rbp
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny(Void*, System.Object)
       test      rax,rax
       jne       near ptr M105_L16
       mov       r14,[rbp]
       mov       rcx,offset MT_System.RuntimeType+CompositeCacheEntry
       cmp       r14,rcx
       je        short M105_L09
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       mov       rcx,offset MT_System.Enum+EnumInfo<System.UInt32>
       cmp       r14,rcx
       jne       near ptr M105_L14
       lea       rcx,[r15+28]
       mov       rdx,rbp
       call      CORINFO_HELP_ASSIGN_REF
M105_L05:
       mov       rcx,rdi
       mov       rdx,r15
       mov       r8,rbp
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       cmp       rax,rbp
       jne       near ptr M105_L00
M105_L06:
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       rax,[rcx+20]
       test      rax,rax
       je        short M105_L10
M105_L07:
       mov       rcx,rsi
       call      rax
       mov       rsi,rax
       mov       rcx,[rbx+30]
       mov       rcx,[rcx]
       mov       rax,[rcx+10]
       test      rax,rax
       je        short M105_L11
M105_L08:
       mov       rcx,r15
       call      rax
       test      rax,rax
       jne       short M105_L12
       jmp       short M105_L15
M105_L09:
       mov       r15,rbp
       jmp       short M105_L06
M105_L10:
       mov       rcx,rbx
       mov       rdx,7FF8AF941F10
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       short M105_L07
M105_L11:
       mov       rcx,rbx
       mov       rdx,7FF8AF8FFCC0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       short M105_L08
M105_L12:
       mov       rcx,rax
       mov       rdx,rsi
       xor       r8d,r8d
       call      System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
       test      rax,rax
       cmove     rax,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
M105_L13:
       mov       rcx,rsi
       call      qword ptr [7FF8AF82C498]; System.RuntimeType.InitializeCache()
       mov       rdi,rax
       jmp       near ptr M105_L01
M105_L14:
       mov       rcx,rbp
       mov       rdx,r15
       mov       r11,7FF8AF573550
       call      qword ptr [r11]
       jmp       near ptr M105_L05
M105_L15:
       call      qword ptr [7FF8B05064C0]
       int       3
M105_L16:
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       ret
; Total bytes of code 442
```
```assembly
; System.Linq.Utilities.CombineSelectors[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Func`2<System.__Canon,System.__Canon>, System.Func`2<System.__Canon,System.__Canon>)
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E4C0]
       mov       rcx,rax
       call      qword ptr [7FF91AA3C670]; CORINFO_HELP_NEWFAST
       mov       rbp,rax
       lea       rcx,[rbp+8]
       mov       rdx,rdi
       call      qword ptr [7FF91AA3C640]; CORINFO_HELP_ASSIGN_REF
       lea       rcx,[rbp+10]
       mov       rdx,rsi
       call      qword ptr [7FF91AA3C640]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,rbx
       call      qword ptr [7FF91AA3E4C8]
       mov       rcx,rax
       call      qword ptr [7FF91AA3C670]; CORINFO_HELP_NEWFAST
       mov       rbx,rax
       mov       rcx,rbx
       mov       rdx,rbp
       call      qword ptr [7FF91AA3F5E0]
       mov       rax,rbx
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 114
```
```assembly
; System.Linq.Enumerable+SelectManySingleSelectorIterator`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].MoveNext()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       xor       eax,eax
       mov       [rsp+28],rax
       mov       [rsp+30],rcx
       mov       rbx,rcx
M107_L00:
       mov       edx,[rbx+14]
       dec       edx
       cmp       edx,2
       ja        near ptr M107_L19
       mov       ecx,edx
       lea       r11,[7FF8B06069F0]
       mov       r11d,[r11+rcx*4]
       lea       rax,[M107_L00]
       add       r11,rax
       jmp       r11
       mov       rcx,[rbx]
       mov       r11,[rcx+30]
       mov       r11,[r11+8]
       cmp       qword ptr [r11+10],68
       jle       short M107_L03
       mov       r11,[r11+68]
       test      r11,r11
       je        short M107_L03
M107_L01:
       mov       rcx,[rbx+18]
       call      qword ptr [r11]
       lea       rcx,[rbx+28]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M107_L02:
       mov       dword ptr [rbx+14],2
       mov       rcx,[rbx+28]
       mov       rdx,offset MT_<>z__ReadOnlySingleElementList<Hl7.Fhir.Model.PocoNode>+Enumerator
       cmp       [rcx],rdx
       jne       short M107_L04
       cmp       byte ptr [rcx+10],0
       jne       near ptr M107_L19
       mov       byte ptr [rcx+10],1
       jmp       short M107_L05
M107_L03:
       mov       rdx,7FF8B065A290
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M107_L01
M107_L04:
       mov       r11,7FF8AF573060
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M107_L19
M107_L05:
       mov       rcx,[rbx]
       mov       r11,[rcx+30]
       mov       r11,[r11+8]
       cmp       qword ptr [r11+10],58
       jle       near ptr M107_L13
       mov       r11,[r11+58]
       test      r11,r11
       je        near ptr M107_L13
M107_L06:
       mov       rcx,[rbx+28]
       call      qword ptr [r11]
       mov       rsi,rax
       mov       rdi,[rbx+20]
       mov       r8,offset Hl7.FhirPath.Functions.CollectionOperators+<>c__DisplayClass14_0.<Navigate>b__0(Hl7.Fhir.Model.PocoNode)
       cmp       [rdi+18],r8
       jne       near ptr M107_L32
       mov       r8,[rdi+8]
       mov       rdi,[r8+8]
       cmp       dword ptr [rdi+8],0
       jbe       near ptr M107_L34
       movzx     ecx,word ptr [rdi+0C]
       cmp       ecx,100
       jae       near ptr M107_L25
       mov       r8d,ecx
       mov       rcx,7FF90A792F18
       test      byte ptr [r8+rcx],40
       jne       near ptr M107_L26
M107_L07:
       cmp       [rsi],sil
       xor       r8d,r8d
       mov       [rsp+28],r8
       mov       rcx,[rsi+10]
       mov       r8,offset MT_Hl7.Fhir.Model.HumanName
       cmp       [rcx],r8
       jne       near ptr M107_L14
       lea       r8,[rsp+28]
       mov       rdx,rdi
       call      qword ptr [7FF8AFC2D350]; Hl7.Fhir.Model.HumanName.TryGetValue(System.String, System.Object ByRef)
M107_L08:
       test      eax,eax
       je        near ptr M107_L27
       mov       rcx,rsi
       mov       rdx,rdi
       mov       r8,[rsp+28]
       call      qword ptr [7FF8B0397198]; Hl7.Fhir.Model.PocoNode.nodeFor(System.String, System.Object)
M107_L09:
       xor       ecx,ecx
       mov       [rsp+28],rcx
       test      rax,rax
       je        near ptr M107_L28
M107_L10:
       mov       rbp,rax
M107_L11:
       mov       rcx,[rbx]
       mov       r11,[rcx+30]
       mov       r11,[r11+8]
       cmp       qword ptr [r11+10],60
       jle       near ptr M107_L15
       mov       r11,[r11+60]
       test      r11,r11
       je        short M107_L15
M107_L12:
       mov       rcx,rbp
       call      qword ptr [r11]
       lea       rcx,[rbx+30]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       dword ptr [rbx+14],3
       mov       rcx,[rbx+30]
       mov       r11,7FF8AF573050
       call      qword ptr [r11]
       test      eax,eax
       jne       short M107_L16
       mov       rcx,[rbx+30]
       mov       r11,7FF8AF573058
       call      qword ptr [r11]
       xor       ecx,ecx
       mov       [rbx+30],rcx
       jmp       near ptr M107_L02
M107_L13:
       mov       rdx,7FF8B065A0B8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       near ptr M107_L06
M107_L14:
       lea       r8,[rsp+28]
       mov       rdx,rdi
       mov       rax,[rcx]
       mov       rax,[rax+48]
       call      qword ptr [rax+20]
       jmp       near ptr M107_L08
M107_L15:
       mov       rdx,7FF8B065A278
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       near ptr M107_L12
M107_L16:
       mov       rcx,[rbx]
       mov       rdx,[rcx+30]
       mov       rdx,[rdx+8]
       cmp       qword ptr [rdx+10],50
       jle       short M107_L18
       mov       r11,[rdx+50]
       test      r11,r11
       je        short M107_L18
M107_L17:
       mov       rcx,[rbx+30]
       call      qword ptr [r11]
       lea       rcx,[rbx+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       eax,1
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M107_L18:
       mov       rdx,offset <PrivateImplementationDetails>.InlineArrayAsReadOnlySpan[[System.Collections.Generic.SegmentedArrayBuilder`1+Arrays[[System.__Canon, System.Private.CoreLib]], System.Linq],[System.__Canon, System.Private.CoreLib]](Arrays<System.__Canon> ByRef, Int32)
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       r11,rax
       jmp       short M107_L17
M107_L19:
       cmp       qword ptr [rbx+30],0
       jne       near ptr M107_L33
M107_L20:
       cmp       qword ptr [rbx+28],0
       jne       short M107_L22
M107_L21:
       xor       eax,eax
       mov       [rbx+8],rax
       mov       dword ptr [rbx+14],0FFFFFFFF
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M107_L22:
       mov       rcx,[rbx+28]
       mov       r11,offset MT_<>z__ReadOnlySingleElementList<Hl7.Fhir.Model.PocoNode>+Enumerator
       cmp       [rcx],r11
       jne       short M107_L24
M107_L23:
       xor       ecx,ecx
       mov       [rbx+28],rcx
       jmp       short M107_L21
M107_L24:
       mov       r11,7FF8AF573068
       call      qword ptr [r11]
       jmp       short M107_L23
M107_L25:
       call      qword ptr [7FF8B05077F8]
       test      eax,eax
       jne       near ptr M107_L07
M107_L26:
       mov       rcx,rsi
       mov       rdx,rdi
       call      qword ptr [7FF8B0397168]
       test      eax,eax
       je        near ptr M107_L07
       jmp       short M107_L29
M107_L27:
       xor       eax,eax
       jmp       near ptr M107_L09
M107_L28:
       mov       rcx,26D3D8042D0
       mov       rax,[rcx]
       jmp       near ptr M107_L10
M107_L29:
       mov       rcx,offset MT_System.Collections.Generic.List<Hl7.Fhir.Model.PocoNode>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       rcx,rbp
       call      qword ptr [7FF8AF9675E8]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]]..ctor()
       inc       dword ptr [rbp+14]
       mov       rcx,[rbp+8]
       mov       edi,[rbp+10]
       cmp       [rcx+8],edi
       ja        short M107_L30
       mov       rcx,rbp
       mov       rdx,rsi
       call      qword ptr [7FF8AF616F40]; System.Collections.Generic.List`1[[System.__Canon, System.Private.CoreLib]].AddWithResize(System.__Canon)
       jmp       short M107_L31
M107_L30:
       lea       edx,[rdi+1]
       mov       [rbp+10],edx
       movsxd    rdx,edi
       mov       r8,rsi
       call      System.Runtime.CompilerServices.CastHelpers.StelemRef(System.Object[], IntPtr, System.Object)
M107_L31:
       jmp       near ptr M107_L11
M107_L32:
       mov       rdx,rsi
       mov       rcx,[rdi+8]
       call      qword ptr [rdi+18]
       mov       rbp,rax
       jmp       near ptr M107_L11
M107_L33:
       mov       rcx,[rbx+30]
       mov       r11,7FF8AF573070
       call      qword ptr [r11]
       xor       eax,eax
       mov       [rbx+30],rax
       jmp       near ptr M107_L20
M107_L34:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 938
```
```assembly
; System.Collections.Generic.SegmentedArrayBuilder`1[[System.__Canon, System.Private.CoreLib]].Expand(Int32)
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,68
       mov       [rsp+60],rdx
       mov       rbx,rcx
       mov       rsi,rdx
       mov       ecx,10
       cmp       r8d,10
       cmovl     r8d,ecx
       mov       ecx,[rbx+100]
       mov       edx,ecx
       add       edx,[rbx+4]
       jo        near ptr M108_L31
       mov       [rbx+4],edx
       cmp       dword ptr [rbx+4],7FFFFFC7
       jg        near ptr M108_L29
       movsxd    rdi,r8d
       movsxd    rcx,ecx
       add       rcx,rcx
       cmp       rdi,rcx
       cmovl     rdi,rcx
       mov       ecx,7FFFFFC7
       cmp       rdi,7FFFFFC7
       cmovg     rdi,rcx
       lea       rbp,[rbx+10]
       mov       r14d,[rbx]
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],108
       jle       near ptr M108_L07
       mov       rcx,[rcx+108]
       test      rcx,rcx
       je        near ptr M108_L07
M108_L00:
       cmp       r14d,1B
       jae       near ptr M108_L30
       mov       eax,r14d
       lea       rbp,[rbp+rax*8]
       call      qword ptr [7FF8AFEA6AA8]; System.Buffers.ArrayPool`1[[System.__Canon, System.Private.CoreLib]].get_Shared()
       mov       r14,rax
       mov       rcx,offset MT_System.Buffers.SharedArrayPool<Hl7.Fhir.Introspection.PropertyMapping>
       cmp       [r14],rcx
       jne       near ptr M108_L26
       mov       r15,r14
       mov       rcx,26D3D8002B8
       mov       r13,[rcx]
       lea       ecx,[rdi-1]
       or        ecx,0F
       xor       r12d,r12d
       lzcnt     r12d,ecx
       xor       r12d,1F
       add       r12d,0FFFFFFFD
       mov       rcx,gs:[58]
       mov       rcx,[rcx+48]
       cmp       dword ptr [rcx+208],0B
       jle       near ptr M108_L10
       mov       rcx,[rcx+210]
       mov       rax,[rcx+58]
       test      rax,rax
       je        near ptr M108_L10
M108_L01:
       mov       rcx,[rax+10]
       test      rcx,rcx
       je        near ptr M108_L12
       mov       edx,[rcx+8]
       cmp       edx,r12d
       jbe       near ptr M108_L12
       mov       edx,r12d
       shl       rdx,4
       mov       r8,[rcx+rdx+10]
       test      r8,r8
       je        near ptr M108_L12
       xor       eax,eax
       mov       [rcx+rdx+10],rax
       cmp       byte ptr [r13+9D],0
       jne       near ptr M108_L11
M108_L02:
       mov       r13,r8
M108_L03:
       mov       rcx,rbp
       mov       rdx,r13
       call      CORINFO_HELP_CHECKED_ASSIGN_REF
       mov       rcx,[rsi+30]
       mov       rcx,[rcx]
       cmp       qword ptr [rcx+8],0E0
       jle       short M108_L08
       mov       rcx,[rcx+0E0]
       test      rcx,rcx
       je        short M108_L08
M108_L04:
       test      r13,r13
       je        near ptr M108_L28
       mov       rdx,[rcx+30]
       mov       rdx,[rdx]
       mov       rax,[rdx+30]
       test      rax,rax
       je        short M108_L09
M108_L05:
       cmp       [r13],rax
       jne       near ptr M108_L27
       lea       rax,[r13+10]
       mov       edx,[r13+8]
M108_L06:
       mov       [rbx+0F8],rax
       mov       [rbx+100],edx
       inc       dword ptr [rbx]
       add       rsp,68
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       ret
M108_L07:
       mov       rcx,rsi
       mov       rdx,7FF8B065D9F0
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       near ptr M108_L00
M108_L08:
       mov       rcx,rsi
       mov       rdx,7FF8B0590820
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       mov       rcx,rax
       jmp       short M108_L04
M108_L09:
       mov       rdx,7FF8B065ABD8
       call      CORINFO_HELP_RUNTIMEHANDLE_CLASS
       jmp       short M108_L05
M108_L10:
       mov       ecx,0B
       call      CORINFO_HELP_GETDYNAMIC_GCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       jmp       near ptr M108_L01
M108_L11:
       mov       [rsp+40],r8
       mov       rcx,r8
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r15d,eax
       mov       rdi,[rsp+40]
       mov       eax,[rdi+8]
       mov       [rsp+50],eax
       mov       rcx,r14
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r9d,eax
       mov       [rsp+20],r12d
       mov       edx,r15d
       mov       r8d,[rsp+50]
       mov       rcx,r13
       call      qword ptr [7FF8B0505068]
       mov       r8,rdi
       jmp       near ptr M108_L02
M108_L12:
       mov       rcx,[r15+10]
       cmp       [rcx+8],r12d
       jbe       near ptr M108_L23
       mov       eax,r12d
       mov       rcx,[rcx+rax*8+10]
       test      rcx,rcx
       je        near ptr M108_L22
       mov       rdi,[rcx+8]
       mov       rcx,offset MT_System.Threading.ProcessorIdCache
       call      CORINFO_HELP_GET_NONGCSTATIC_BASE
       cmp       byte ptr [7FF8AF56B144],0
       jne       short M108_L14
       mov       ecx,0C
       call      CORINFO_HELP_GETDYNAMIC_NONGCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       mov       r14d,[rax+10]
       mov       ecx,0C
       call      CORINFO_HELP_GETDYNAMIC_NONGCTHREADSTATIC_BASE_NOCTOR_OPTIMIZED
       lea       ecx,[r14-1]
       mov       [rax+10],ecx
       movzx     eax,r14w
       test      eax,eax
       je        short M108_L13
       sar       r14d,10
       jmp       short M108_L15
M108_L13:
       call      qword ptr [7FF8B0505050]
       mov       r14d,eax
       jmp       short M108_L15
M108_L14:
       call      qword ptr [7FF8B0505038]
       mov       r14d,eax
M108_L15:
       mov       rcx,offset MT_System.Buffers.SharedArrayPoolStatics
       call      CORINFO_HELP_GET_NONGCSTATIC_BASE
       mov       eax,r14d
       xor       edx,edx
       div       dword ptr [7FF8AF56B138]
       mov       r14d,edx
       xor       eax,eax
M108_L16:
       mov       [rsp+4C],eax
       cmp       [rdi+8],eax
       jle       near ptr M108_L20
       cmp       r14d,[rdi+8]
       jae       near ptr M108_L30
       mov       ecx,r14d
       mov       rdx,[rdi+rcx*8+10]
       mov       [rsp+30],rdx
       cmp       [rdx],dl
       xor       r8d,r8d
       mov       [rsp+38],r8
       mov       rcx,rdx
       call      System.Threading.Monitor.Enter(System.Object)
       mov       rdx,[rsp+30]
       mov       rcx,[rdx+8]
       mov       eax,[rdx+10]
       dec       eax
       cmp       [rcx+8],eax
       jbe       short M108_L17
       mov       r8d,eax
       mov       r8,[rcx+r8*8+10]
       mov       [rsp+38],r8
       mov       r10d,eax
       xor       r9d,r9d
       mov       [rcx+r10*8+10],r9
       mov       [rdx+10],eax
M108_L17:
       mov       rcx,rdx
       call      System.Threading.Monitor.Exit(System.Object)
       mov       rcx,[rsp+38]
       test      rcx,rcx
       je        short M108_L18
       jmp       short M108_L21
M108_L18:
       inc       r14d
       cmp       [rdi+8],r14d
       jne       short M108_L19
       xor       r14d,r14d
M108_L19:
       mov       eax,[rsp+4C]
       inc       eax
       jmp       near ptr M108_L16
M108_L20:
       xor       ecx,ecx
M108_L21:
       mov       rdi,rcx
       test      rdi,rdi
       je        short M108_L22
       cmp       byte ptr [r13+9D],0
       mov       r8,rdi
       je        near ptr M108_L02
       mov       [rsp+40],r8
       mov       rcx,r8
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       edi,eax
       mov       r14,[rsp+40]
       mov       eax,[r14+8]
       mov       [rsp+54],eax
       mov       rcx,r15
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r9d,eax
       mov       [rsp+20],r12d
       mov       edx,edi
       mov       r8d,[rsp+54]
       mov       rcx,r13
       call      qword ptr [7FF8B0505068]
       mov       r8,r14
       jmp       near ptr M108_L02
M108_L22:
       mov       ecx,10
       shlx      edi,ecx,r12d
       jmp       short M108_L24
M108_L23:
       test      edi,edi
       je        near ptr M108_L25
       mov       ecx,edi
       mov       rdx,26D002D2160
       call      qword ptr [7FF8AF967540]; System.ArgumentOutOfRangeException.ThrowIfNegative[[System.Int32, System.Private.CoreLib]](Int32, System.String)
M108_L24:
       movsxd    rdx,edi
       mov       rcx,offset MT_Hl7.Fhir.Introspection.PropertyMapping[]
       call      CORINFO_HELP_NEWARR_1_OBJ
       mov       r14,rax
       cmp       byte ptr [r13+9D],0
       mov       r8,r14
       je        near ptr M108_L02
       mov       [rsp+40],r8
       mov       rcx,r8
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       edi,eax
       mov       r14,[rsp+40]
       mov       eax,[r14+8]
       mov       [rsp+5C],eax
       mov       rcx,r15
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r9d,eax
       mov       dword ptr [rsp+20],0FFFFFFFF
       mov       edx,edi
       mov       r8d,[rsp+5C]
       mov       rcx,r13
       call      qword ptr [7FF8B0505068]
       mov       eax,[r14+8]
       mov       [rsp+58],eax
       mov       rcx,r15
       call      System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
       mov       r9d,eax
       mov       rcx,[r15+10]
       mov       edx,1
       mov       r8d,2
       cmp       [rcx+8],r12d
       cmovg     edx,r8d
       mov       dword ptr [rsp+20],0FFFFFFFF
       mov       [rsp+28],edx
       mov       rcx,r13
       mov       edx,edi
       mov       r8d,[rsp+58]
       call      qword ptr [7FF8B0505080]
       mov       r8,r14
       jmp       near ptr M108_L02
M108_L25:
       mov       rcx,offset MT_System.Array+EmptyArray<Hl7.Fhir.Introspection.PropertyMapping>
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rcx,26D3D8047B0
       mov       r13,[rcx]
       jmp       near ptr M108_L03
M108_L26:
       mov       rcx,r14
       mov       edx,edi
       mov       rax,[r14]
       mov       rax,[rax+40]
       call      qword ptr [rax+20]
       mov       r13,rax
       jmp       near ptr M108_L03
M108_L27:
       call      qword ptr [7FF8B050C0A8]
       int       3
M108_L28:
       xor       eax,eax
       xor       edx,edx
       jmp       near ptr M108_L06
M108_L29:
       mov       rcx,offset MT_System.OutOfMemoryException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       mov       rcx,rbx
       call      qword ptr [7FF8B050C4C8]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M108_L30:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
M108_L31:
       call      CORINFO_HELP_OVERFLOW
       int       3
; Total bytes of code 1344
```
```assembly
; System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan[[System.__Canon, System.Private.CoreLib]](System.__Canon ByRef, Int32)
       mov       [rcx],r8
       mov       [rcx+8],r9d
       mov       rax,rcx
       ret
; Total bytes of code 11
```
```assembly
; System.Array.Clear(System.Array)
       sub       rsp,28
       test      rcx,rcx
       je        short M110_L01
       mov       rdx,[rcx]
       movzx     eax,word ptr [rdx]
       mov       r8d,[rcx+8]
       imul      r8,rax
       mov       rax,[rcx]
       mov       eax,[rax+4]
       lea       rcx,[rcx+rax-8]
       test      dword ptr [rdx],1000000
       je        short M110_L00
       mov       rdx,r8
       shr       rdx,3
       lea       rax,[System.Collections.Generic.CollectionExtensions.GetValueOrDefault[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]](System.Collections.Generic.IReadOnlyDictionary`2<System.__Canon,System.__Canon>, System.__Canon, System.__Canon)]
       add       rsp,28
       jmp       qword ptr [rax]
M110_L00:
       mov       rdx,r8
       call      qword ptr [7FF90B384C60]; Precode of System.SpanHelpers.ClearWithoutReferences(Byte ByRef, UIntPtr)
       nop
       add       rsp,28
       ret
M110_L01:
       mov       ecx,2
       call      qword ptr [7FF90B3866F8]
       int       3
; Total bytes of code 90
```
```assembly
; System.Buffers.ArrayPool`1[[System.__Canon, System.Private.CoreLib]].get_Shared()
       sub       rsp,28
       mov       [rsp+20],rcx
       call      CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rax,[rax]
       add       rsp,28
       ret
; Total bytes of code 22
```
```assembly
; System.Buffers.SharedArrayPool`1[[System.__Canon, System.Private.CoreLib]].InitializeTlsBucketsAndTrimming()
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,38
       mov       [rsp+30],rcx
       mov       rbx,rcx
       mov       ecx,1B
       call      qword ptr [7FF90B37E1B0]
       mov       rsi,rax
       mov       rcx,[rbx]
       call      qword ptr [7FF90B373500]
       mov       rcx,rax
       call      qword ptr [7FF90B3711C0]; CORINFO_HELP_GET_GCTHREADSTATIC_BASE
       lea       rcx,[rax+10]
       mov       rdx,rsi
       call      qword ptr [7FF90B3710F0]; CORINFO_HELP_ASSIGN_REF
       mov       rcx,[rbx+8]
       mov       rdx,rsi
       xor       r8d,r8d
       cmp       [rcx],ecx
       call      qword ptr [7FF90B3969D0]; Precode of System.Runtime.CompilerServices.ConditionalWeakTable`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].Add(System.__Canon, System.__Canon)
       lea       rcx,[rbx+18]
       mov       eax,1
       xchg      al,[rcx]
       movzx     eax,al
       test      eax,eax
       jne       short M112_L01
       mov       rcx,[rbx]
       call      qword ptr [7FF90B373508]
       mov       rcx,rax
       call      qword ptr [7FF90B3711B0]; CORINFO_HELP_GET_GCSTATIC_BASE
       mov       rdi,rax
       mov       rbp,[rdi+8]
       test      rbp,rbp
       jne       short M112_L00
       call      qword ptr [7FF90B37C620]
       mov       rbp,rax
       mov       rdx,[rdi]
       mov       rcx,rbp
       call      qword ptr [7FF90B380D38]
       lea       rcx,[rdi+8]
       mov       rdx,rbp
       call      qword ptr [7FF90B3710F0]; CORINFO_HELP_ASSIGN_REF
M112_L00:
       call      qword ptr [7FF90B37AF18]
       mov       rdi,rax
       lea       rcx,[rdi+10]
       mov       rdx,rbp
       call      qword ptr [7FF90B3710F0]; CORINFO_HELP_ASSIGN_REF
       xor       ecx,ecx
       mov       [rsp+28],rcx
       lea       rcx,[rsp+28]
       mov       rdx,rbx
       xor       r8d,r8d
       call      qword ptr [7FF90B38B6B8]; Precode of System.Runtime.InteropServices.GCHandle..ctor(System.Object, System.Runtime.InteropServices.GCHandleType)
       mov       rax,[rsp+28]
       mov       [rdi+18],rax
M112_L01:
       mov       rax,rsi
       add       rsp,38
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
; Total bytes of code 228
```
```assembly
; System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].GetOrAdd[[System.__Canon, System.Private.CoreLib]](System.__Canon, System.Func`3<System.__Canon,System.__Canon,System.__Canon>, System.__Canon)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,60
       lea       rbp,[rsp+90]
       xor       eax,eax
       mov       [rbp-40],rax
       mov       [rbp-38],rdx
       mov       rsi,rcx
       mov       rdi,rdx
       mov       rbx,r8
       mov       r14,r9
       test      rbx,rbx
       je        near ptr M113_L05
       test      r14,r14
       je        near ptr M113_L04
       mov       r15,[rsi+8]
       mov       r13,[r15+8]
       cmp       byte ptr [rsi+15],0
       jne       short M113_L02
       mov       rcx,[rsi]
       call      qword ptr [7FF924A7FB30]
       mov       rcx,r13
       mov       r11,rax
       mov       rdx,rbx
       call      qword ptr [rax]
       mov       r13d,eax
M113_L00:
       mov       rcx,rdi
       call      qword ptr [7FF924A7FBA8]
       mov       rcx,rax
       lea       rdx,[rbp-40]
       mov       [rsp+20],rdx
       mov       rdx,r15
       mov       r8,rbx
       mov       r9d,r13d
       call      qword ptr [7FF924A807B8]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryGetValueInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, Int32, System.__Canon ByRef)
       test      eax,eax
       je        short M113_L03
M113_L01:
       mov       rax,[rbp-40]
       add       rsp,60
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M113_L02:
       mov       rcx,rbx
       lea       r11,[7FF924A7F0A8]
       call      qword ptr [r11]
       mov       r13d,eax
       jmp       short M113_L00
M113_L03:
       mov       byte ptr [rbp-48],1
       mov       [rbp-44],r13d
       mov       rdx,rbx
       mov       r8,[rbp+30]
       mov       rcx,[r14+8]
       call      qword ptr [r14+18]
       xor       r9d,r9d
       mov       [rsp+28],r9d
       mov       dword ptr [rsp+30],1
       lea       r9,[rbp-40]
       mov       [rsp+38],r9
       mov       [rsp+20],rax
       mov       r9,[rbp-48]
       mov       r8,rbx
       mov       rdx,r15
       mov       rcx,rsi
       call      qword ptr [7FF924A807E0]; Precode of System.Collections.Concurrent.ConcurrentDictionary`2[[System.__Canon, System.Private.CoreLib],[System.__Canon, System.Private.CoreLib]].TryAddInternal(Tables<System.__Canon,System.__Canon>, System.__Canon, System.Nullable`1<Int32>, System.__Canon, Boolean, Boolean, System.__Canon ByRef)
       jmp       short M113_L01
M113_L04:
       mov       rcx,[7FF924A80D78]
       mov       rcx,[rcx]
       call      qword ptr [7FF924A80208]
       int       3
M113_L05:
       mov       rcx,[7FF924A80C30]
       mov       rcx,[rcx]
       call      qword ptr [7FF924A80208]
       int       3
; Total bytes of code 284
```
**Extern method**
System.RuntimeTypeHandle.GetAssembly(System.RuntimeType)
System.Object.GetType()
System.Threading.Interlocked.CompareExchangeObject(System.Object ByRef, System.Object, System.Object)
System.Buffer.__BulkMoveWithWriteBarrier(Byte ByRef, Byte ByRef, UIntPtr)
System.Threading.Monitor.ReliableEnter(System.Object, Boolean ByRef)
System.Threading.Monitor.Exit(System.Object)
System.Runtime.CompilerServices.CastHelpers.ChkCastAny_NoCacheLookup(Void*, System.Object)
System.Runtime.CompilerServices.CastHelpers.IsInstanceOfAny_NoCacheLookup(Void*, System.Object)
System.Threading.Monitor.Enter(System.Object)
System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)

