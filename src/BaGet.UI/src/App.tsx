import * as React from 'react';
import { BrowserRouter as Router, NavLink, Route, RouteComponentProps, Switch } from 'react-router-dom';
import './App.css';
import DisplayPackage from './DisplayPackage/DisplayPackage';
import SearchResults from './SearchResults';
import Upload from './Upload';

interface IAppState {
  input: string;
  selected?: string;
}

class App extends React.Component<{}, IAppState> {

  constructor(props: {}) {
    super(props);

    this.state = { input: "" };
  }

  public render() {
    return (
      <Router>
        <div>
          {this._renderNavigationBar()}

          {this._renderContent()}
        </div>
      </Router>
    );
  }

  private _renderNavigationBar() {
    return (
      <nav className="navbar navbar-inverse" role="navigation">
        <div className="container">
          <div className="row">
            <div id="navbar" className="col-sm-12">
              <ul className="nav navbar-nav" role="tablist">
                <li role="presentation"><NavLink to="/" exact={true} role="tab"><span>Packages</span></NavLink></li>
                <li role="presentation"><NavLink to="/upload"><span>Upload</span></NavLink></li>
                <li role="presentation"><a role="tab" href="https://loic-sharma.github.io/BaGet/"><span>Documentation</span></a></li>
              </ul>
            </div>
          </div>
        </div>
        <div className="container search-container">
          <div className="row">
            <form className="col-sm-12">
              <input
                type="text"
                className="form-control"
                autoComplete="off"
                placeholder="Search packages..."
              onChange={this.handleChange} />
            </form>
          </div>
        </div>
      </nav>
    );
  }

  private _renderContent() {
    return (
        <section role="main" className="container main-container">
          <Switch>
            <Route exact={true} path="/" render={this.renderSearch} />
            <Route path="/packages/:id" component={DisplayPackage} />
            <Route path="/upload" component={Upload} />
          </Switch>
        </section>
      );
  }

  private renderSearch = (props: RouteComponentProps<any>) => (
    <SearchResults input={this.state.input} {...props} />
  );

  private handleChange = (input: React.ChangeEvent<HTMLInputElement>) =>
    this.setState({ input: input.target.value, selected: undefined });
}

export default App;
