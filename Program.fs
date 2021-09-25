open FSharp.Control.Tasks
open Giraffe
open Saturn
open Saturn.Endpoint.Router
open Microsoft.AspNetCore.Http
open System
open System.IO
open FSharp.Control.Reactive

type INotifierService =
    inherit IDisposable
    abstract OnFileChanged : IObservable<string>

let getNotifier (path: string) : INotifierService =
    let fsw = new FileSystemWatcher(path)

    fsw.NotifyFilter <- NotifyFilters.FileName ||| NotifyFilters.Size
    fsw.EnableRaisingEvents <- true

    let changed =
        fsw.Changed
        |> Observable.map (fun args -> args.Name)

    let deleted =
        fsw.Deleted
        |> Observable.map (fun args -> args.Name)

    let renamed =
        fsw.Renamed
        |> Observable.map (fun args -> args.Name)

    let created =
        fsw.Created
        |> Observable.map (fun args -> args.Name)

    let obs =
        Observable.mergeSeq [ changed
                              deleted
                              renamed
                              created ]

    { new INotifierService with
        override _.Dispose() : unit = fsw.Dispose()
        override _.OnFileChanged: IObservable<string> = obs }

let sse next (ctx: HttpContext) =
    task {
        let res = ctx.Response
        ctx.SetStatusCode 200
        ctx.SetHttpHeader("Content-Type", "text/event-stream")
        ctx.SetHttpHeader("Cache-Control", "no-cache")
        let notifier = getNotifier @"C:\Users\scyth\Desktop"

        let onFileChanged =
            notifier.OnFileChanged
            |> Observable.subscribe
                (fun filename ->
                    task {
                        do! res.WriteAsync $"event:reload\ndata:{filename}\n\n"
                        do! res.Body.FlushAsync()
                    }
                    |> Async.AwaitTask
                    |> Async.Start)

        do! res.WriteAsync($"event:start\ndata:{DateTime.Now}\n\n")
        do! res.Body.FlushAsync()

        ctx.RequestAborted.Register
            (fun _ ->
                notifier.Dispose()
                onFileChanged.Dispose())
        |> ignore

        while true do
            do! Async.Sleep(TimeSpan.FromSeconds 1.)

        return! text "" next ctx
    }

let appRouter = router { get "/sse" sse }

[<EntryPoint>]
let main args =
    let app =
        application {
            use_endpoint_router appRouter
            use_static "wwwroot"
        }

    run app
    0
