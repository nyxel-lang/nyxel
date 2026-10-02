# 架构总览

依据：ADR-0004（编译模型）。本文描述各组件的职责和依赖方向；编译器内部各阶段的细节在实现时各自写设计文档。

## 编译流水线

```
.nyxel 源码
  │  Nyxel.Compiler
  ├─ 语法    SourceText → Lexer → Parser → SyntaxTree（含语法诊断）
  ├─ 语义    Binder：名字解析、类型检查、Nyxel 语义规则 → 绑定树（含语义诊断）
  │          .NET 类型信息通过 Roslyn 的符号模型读取引用的程序集（细节 M2 定）
  ├─ 降级    绑定树 → C#，每条语句带 #line 跨度映射回 .nyxel（规则见 debug-mapping.md）
  └─ 后端    Roslyn CSharpCompilation → .dll + portable PDB
             Roslyn 的诊断映射回 .nyxel 位置，按编译器内部错误报出
  ▼
普通 .NET 程序集（引用 Nyxel.Runtime），可被宿主加载、被 C# 引用
```

诊断从前端报出：错误码稳定、用 Nyxel 术语、位置指向 .nyxel。`--emit-cs` 输出降级后的 C#，用于查看和快照测试。

## 组件

| 项目 | 职责 | 依赖 |
|---|---|---|
| Nyxel.Compiler | 上面整条流水线，作为库提供给其他组件 | Roslyn（`Microsoft.CodeAnalysis.CSharp`） |
| Nyxel.Runtime | 生成代码调用的辅助类型和函数；被加载进游戏进程 | 无（AOT 兼容） |
| Nyxel.Host | 引擎嵌入用：可卸载 AssemblyLoadContext 加载脚本、热重载、开发期进程内编译 | Runtime、Compiler |
| Nyxel.Sdk | MSBuild 项目 SDK，让 `.nyxelproj` 可以 `dotnet build` | 构建时调用 Compiler |
| Nyxel.Cli | `nyxel` 命令行：build / run / --emit-cs 等 | Compiler |
| tools/vscode-nyxel | 语法高亮、文件关联，之后加 LSP 客户端和断点支持 | — |

依赖方向：Runtime 最底层，谁都不依赖；Compiler 不依赖 Runtime 项目（编译用户代码时把 Nyxel.Runtime.dll 作为元数据引用传给 Roslyn）；Host、Sdk、Cli 在上面。

## 运行时的样子

发布时，游戏进程里只有编译好的脚本程序集和 Nyxel.Runtime，没有编译器。开发期，宿主可以通过 Nyxel.Host 常驻 Roslyn，保存 .nyxel 后在进程内重新编译，卸载旧的 AssemblyLoadContext，加载新的（热重载的状态迁移规则 M4 设计）。
