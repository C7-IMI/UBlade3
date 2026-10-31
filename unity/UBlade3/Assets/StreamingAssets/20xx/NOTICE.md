# HyperBlade 20XX runtime

This bundled runtime is the prebuilt Windows release 0.3.1 of
https://github.com/zach2good/hyperblade-20XX, licensed LGPL-3.0. It is the exact modernised
implementation of the original HyperBlade engine, used by the UBlade3 Unity host as the playable
exact runtime while the C# port advances.

The bundle contains no original HyperBlade game data. The host stages it from a user-supplied
installation (`HYPERBLADE_GAME_DATA`, default `D:\Games\HyperBlade\HYB`) and never commits it.
