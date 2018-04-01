import * as React from "react";

interface AppState {
}

export default class App extends React.Component<{}, AppState> {
    render(): JSX.Element {
        return (
            <div>Hello world</div>
        )
    }
}