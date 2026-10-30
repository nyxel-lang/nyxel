# ADR-0032: 哪些写法是 struct 的副本、`var self` 扩展、C# 里没标 `readonly` 的方法

- 状态：已接受
- 日期：2026-10-30

## 背景

第二十五轮讨论，“待定”的第六轮第二批：struct 的修改。三项连在一起：

- ADR-0008 决策 6 说“属性、索引器、函数调用取出来的 struct 是一份副本”，不能在上面调用 `var self` 方法。但 C# 里数组元素、`Span<T>` 的元素、返回 `ref` 的函数拿到的是原来的存储，不是副本，这条措辞把它们也算进去了。
- 扩展里能不能写 `var self`（ADR-0014 待定）。
- C# 里没标 `readonly` 的 struct 方法，在 `let` 和副本上怎么调用（ADR-0008 待定）。

查了 EnginePlayground：

- 每帧的旋转系统（samples/Sandbox/Scripts/Spinner.cs）在 `Span<Transform>` 的元素上直接改：`transforms[i].Rotation = ...`，`chunk.Get<T>()` 返回指向引擎内存的 `Span<T>`。`world.Get<T>(e)` 返回 `ref T`。
- 查询构建器 `Query` 的 `Read<T>()`、`Without<T>()` 没标 `readonly`：它们修改自己，再返回自己的一份副本，用法是在临时值上连着调用（`world.Query<Transform>().Read<Spin>()`）。`ChunkEnumerator.MoveNext` 也没标。`World`、`Commands`、`Entity` 是 `readonly struct`。
- 没有用到 `ref var`（ref 局部变量）。

System.Numerics 的 `Vector3`、`Matrix4x4`、`Quaternion` 的实例方法都标了 `readonly`，`DateTime` 这类是整个 `readonly struct`。

用 Roslyn 5.9（C# 14）验证过：

- 返回 `ref` 的函数的结果、`Span<T>` 的元素、数组元素可以赋值，也可以调用 `ref` 接收者的扩展；`ReadOnlySpan<T>` 的元素不行（CS8332、CS8329）。
- 给副本的字段赋值是错误：属性的结果和 `List<T>` 的 `[]` 是 CS1612，`foreach` 的变量是 CS1654。
- `ref` 接收者的扩展在属性上调用是 CS0206，在只读字段上是 CS0192；只能声明在 struct 上（CS9300），`T: struct` 可以。
- 没标 `readonly` 的方法在 `foreach` 的变量、`in` 参数、只读字段、函数返回值上调用时，C# 不报错，悄悄复制一份再调用。运行结果：`foreach (var c in list) c.Bump();` 之后列表里的值没变。

三项都按推荐定。

## 决策

1. **哪些写法拿到的是副本，同 C#。** 数组元素、`Span<T>` 的元素、返回 `ref` 的函数指向原来的存储，可以直接改。

   ```nyxel
   for chunk in world.Query<Transform>().Read<Spin>() {
       let transforms = chunk.Get<Transform>()          // Span<Transform>：直接指向引擎的内存
       let spins = chunk.GetReadOnly<Spin>()            // ReadOnlySpan<Spin>：只读
       for i in 0..<chunk.Count {
           transforms[i].Rotation = Quaternion.Normalize(...)    // 写进引擎的内存，不复制
       }
   }
   ```

   | 写法 | 拿到的是 | 能改吗 |
   |---|---|---|
   | `var` 局部变量；class 的字段；`var self` 方法里的 `self.x` | 存储本身 | 能 |
   | `let` 局部变量和字段；`self` 方法里的 `self.x`；`for x in` 的 `x` | 存储本身，只读 | 不能 |
   | 数组元素 `arr[i]` | 数组里的格子 | 能，`let arr` 也能（`let` 只是不能换成另一个数组） |
   | `Span<T>` 的元素、返回 `ref` 的函数（`world.Get<T>(e)`） | 原来的存储 | 能，`let` 的 `Span` 也能 |
   | `ReadOnlySpan<T>` 的元素、返回 `ref readonly` 的函数 | 原来的存储，只读 | 不能 |
   | 属性 `obj.Position`、普通索引器 `list[i]` / `dict[key]`、普通函数的返回值 | 副本 | 不能，改了会丢 |

   - “改”指给它的字段赋值、调用它的 `var self` 方法（ADR-0008 决策 6）。
   - 副本上要改，先存进 `var` 局部变量，改完再写回去，诊断给出这个写法。
   - 没有 ref 局部变量（C# 的 `ref var t = ref world.Get<Transform>(e)`）。`var t = world.Get<Transform>(e)` 是复制一份。要连改几个字段，调用几次 `Get`，或者写一个 `var self` 方法（决策 2）。

2. **扩展里能写 `var self`，规则同 struct 的方法（ADR-0008 决策 6）。**

   ```nyxel
   extension Transform {
       public func MoveBy(var self, delta: Vector3) {
           self.Position += delta
       }
       public func Forward(self) -> Vector3 = Vector3.Transform(Vector3.UnitZ, self.Rotation)
   }

   world.Get<Transform>(e).MoveBy(delta)     // ref 返回值：改的是引擎里的组件
   transforms[i].MoveBy(delta)               // Span 的元素
   var t = Transform.Identity
   t.MoveBy(delta)                           // var 局部变量

   let fixed = Transform.Identity
   fixed.MoveBy(delta)                       // 错误：fixed 是 let
   self.ghosts[0].MoveBy(delta)              // 错误（ghosts 是 List）：改的是副本
   ```

   - 只能扩展 struct（`Vector3`、`Transform`，或泛型扩展里有 `struct` 约束的 T）。被扩展的是 class 时写 `var self` 是编译错误，理由同 ADR-0008：class 方法总能改字段。
   - 只写 `self` 的扩展方法不能改自己的字段，也不能调用 `var self` 方法，同 struct 的 `self` 方法。
   - 在哪里能调用按决策 1 的表。
   - 翻译：C# 14 的 `extension(ref Transform self)`。只写 `self` 的方法放在另一个 `extension(Transform self)` 块里，按值还是按 `in` 传由编译器决定。

3. **C# 里没标 `readonly` 的 struct 方法同 C#，但在只读的值或副本上调用、又没用它的返回值时，是编译错误。**

   ```csharp
   public struct Cooldown {                        // C# 库里的 struct，方法都没标 readonly
       public float Left;
       public void Tick(float dt) { Left -= dt; }  // 会改自己
       public bool Ready() => Left <= 0;           // 不改自己，只是没标
   }
   ```

   ```nyxel
   for chunk in world.Query<Transform>().Read<Spin>() { ... }   // 能：每一步的返回值都用上了
   let ready = cd.Ready()                                         // 能：用了返回值

   for cd in self.cooldowns { cd.Tick(dt) }    // 错误：Tick 没标 readonly，又不用返回值，只能是在改 cd，而 cd 是只读的
   self.weapon.Cooldown.Tick(dt)               // 错误：Cooldown 是属性，改的是副本

   var cd = self.weapon.Cooldown               // 诊断给出的写法
   cd.Tick(dt)
   self.weapon.Cooldown = cd
   ```

   - “只读的值或副本”就是决策 1 表里不能改的那几行。在 `var` 局部变量、数组元素这些能改的位置上调用照常。
   - 道理：在只读的值或副本上调用一个方法又不要它的返回值，只能是为了副作用。如果它改自己，改动一定会丢；如果不改自己，只是库作者漏标了 `readonly`，存进 `var` 变量再调用就行。
   - 用了返回值的照 C# 处理：在一份副本上调用。查询构建器 `Read<T>()` 这类“改自己再返回自己”的写法因此能用。
   - 标了 `readonly` 的方法、`readonly struct` 的方法同 Nyxel 的 `self` 方法，哪里都能调用。Nyxel 自己的方法有准确的信息（`self` 是 `readonly`，`var self` 不是），这条只管 C# 的方法。
   - 只查确定是 struct 的值；没有 `struct` 约束的类型参数 T 不查（多半是 class）。
   - 属性的 getter 不受影响，取值的结果总是用上的。
   - 误报：没标 `readonly` 的句柄类 struct 的无返回值方法（`let h = ...; h.Play()`），要改写成 `var h`。引擎的绑定层应该给不改自己的 struct 标 `readonly`，EnginePlayground 的 `World`、`Commands` 已经是这样。
   - 漏报：用了返回值、同时又改了自己的方法，比如在 `let` 的枚举器上 `while e.MoveNext()` 会死循环。`for in` 会处理枚举，手写枚举器很少见。

## 备选方案

- 副本的范围：
  - **只有变量和字段能改，所有 `[]` 和函数调用都当副本**（ADR-0008 原来的措辞“索引器取出来的是副本”）：一句话讲完，但 `transforms[i].Rotation = ...` 要写成复制出来、改、写回三行，每个实体每帧多复制两次；粒子数组这类“struct 数组逐个改”的写法都一样；`world.Get<T>(e)` 改完还要找引擎的 `Set` 写回去。
- `var self` 扩展：
  - **不支持，要“改”就返回新值**（`t = t.MovedBy(delta)`）：System.Numerics 的 `Vector3` 是这种风格，但 ECS 组件是原地改的，`world.Get<Transform>(e) = world.Get<Transform>(e).MovedBy(delta)` 要找两次，还多了复制。
- 没标 `readonly` 的方法：
  - **全当 `var self`**：最安全，但 `cd.Ready()` 这种不改自己的也不能在 `let` 上调用；`world.Query<Transform>().Read<Spin>()` 是在临时值上调用，EnginePlayground 的主要 API 就用不了。
  - **同 C#**：哪里都能调用，在只读的值和副本上悄悄复制，`for cd in self.cooldowns { cd.Tick(dt) }` 编译通过但什么也没发生，就是 Unity 的 `transform.position.Normalize()` 那个坑。

## 后果

- 修改了 ADR-0008 决策 6（副本的范围：数组元素、`Span` 元素、`ref` 返回值不是副本）。原文已改成最新，见它的修订记录。ADR-0008 和 ADR-0014 的待定由本 ADR 定了。
- 这一轮全是语义规则，Parser 不用改：扩展里的 `var self` 本来就能读。都留给语义分析（M2）：每个表达式是存储、只读的存储还是副本；在副本和只读的值上赋值、调用 `var self` 方法；没标 `readonly` 的 C# 方法的返回值有没有用上；`var self` 扩展的被扩展类型是不是 struct。
- 翻译（M2 / M3）：`var self` 扩展放进 `extension(ref T self)` 块。
- 样例 samples/Ecs/Spin.nyxel：照 EnginePlayground 的旋转系统写的，`Span` 元素上调用 `var self` 扩展、`world.Get<T>(e)` 的 `ref` 返回值、查询构建器。
- 待定：ref 局部变量（`ref var t = ref world.Get<Transform>(e)`）。
