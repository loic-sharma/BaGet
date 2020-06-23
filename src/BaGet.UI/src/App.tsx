import * as React from 'react';

import { Action, Location } from 'history';
import { withRouter } from 'react-router';
import { NavLink, Route, RouteComponentProps } from 'react-router-dom';

import SearchResults from './SearchResults';

import './App.css';

interface IAppState {
  input: string;
}

class App extends React.Component<RouteComponentProps, IAppState> {

  constructor(props: RouteComponentProps) {
    super(props);

    this.state = { input: "" };

    this.props.history.listen(this.onRouteChange);
  }

  public render() {
    return (
      <div>
        {this._renderNavigationBar()}

        {this._renderContent()}
		
		{this._renderFooter()}
      </div>
    );
  }

  private onRouteChange = (location: Location, action: Action) =>
    this.setState({ input: "" });

  private _renderFooter() {
    return (
      <footer className="footer">
	    <div className="container">
          <div className="row">
            <div className="col-md-3 row-gap">
            </div>
            <div className="col-md-9 row-gap">
              <div className="row">
                <div className="col-md-12 footer-release-info">
                  <p>
                    © BaGet 2020 -
                    <a href="/policies/About">About</a> -
                    <a href="/policies/Terms">Terms of Use</a> -
                    <a href="https://go.microsoft.com/fwlink/?LinkId=521839">Privacy Policy</a>
                    - <a href="https://www.microsoft.com/trademarks">Trademarks</a>
                    <br />
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
	  </footer>
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
                <li role="presentation">
                  <a role="tab" href="https://loic-sharma.github.io/BaGet/" target="_blank" rel="noopener noreferrer">
                    <span>Documentation</span>
                  </a>
                </li>
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
    if (this.state.input.length === 0) {
      return (
        <section role="main" className="container main-container">
          <Route exact={true} path="/" render={this.renderSearch} />

          {this.props.children}
        </section>
      );
    }
    else
    {
      return (
        <section role="main" className="container main-container">
          <SearchResults input={this.state.input} />
        </section>
      );
    }
  }

  private renderSearch = (props: RouteComponentProps<any>) => (
    <SearchResults input={this.state.input} {...props} />
  );

  private handleChange = (input: React.ChangeEvent<HTMLInputElement>) =>
    this.setState({ input: input.target.value });
}

export default withRouter(App);
