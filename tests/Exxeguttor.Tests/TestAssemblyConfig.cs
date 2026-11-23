using Xunit;

// Las pruebas comparten PokemonDatabase.Instance, cuya conexión SQLite no admite uso concurrente (el app la usa desde un
// solo hilo y las tareas en segundo plano abren una conexión propia). En paralelo daba errores intermitentes de
// «Index was out of range» en SqliteConnection.RemoveCommand.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
